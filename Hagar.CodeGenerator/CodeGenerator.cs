﻿using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Hagar.CodeGenerator.SyntaxGeneration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Hagar.CodeGenerator
{
    internal interface IMemberDescription
    {
        uint FieldId { get; }

        ISymbol Member { get; }
        ITypeSymbol Type { get; }
    }

    internal class FieldDescription : IMemberDescription
    {
        public FieldDescription(uint fieldId, IFieldSymbol field)
        {
            this.FieldId = fieldId;
            this.Field = field;
        }

        public IFieldSymbol Field { get; }
        public uint FieldId { get; }
        public ISymbol Member => this.Field;
        public ITypeSymbol Type => this.Field.Type;
    }

    internal class PropertyDescription : IMemberDescription
    {
        public PropertyDescription(uint fieldId, IPropertySymbol property)
        {
            this.FieldId = fieldId;
            this.Property = property;
        }

        public uint FieldId { get; }
        public ISymbol Member => this.Property;
        public ITypeSymbol Type => this.Property.Type;
        public IPropertySymbol Property { get; }
    }

    internal class TypeDescription
    {
        public TypeDescription(INamedTypeSymbol type, IEnumerable<IMemberDescription> members)
        {
            this.Type = type;
            this.Members = members.ToList();
        }

        public INamedTypeSymbol Type { get; }

        public List<IMemberDescription> Members { get; }
    }
    
    public class CodeGenerator
    {
        internal const string CodeGeneratorName = "HagarGen";
        private readonly Compilation compilation;
        private readonly INamedTypeSymbol generateSerializerAttribute;
        private readonly INamedTypeSymbol fieldIdAttribute;

        public CodeGenerator(Compilation compilation)
        {
            this.compilation = compilation;
            this.generateSerializerAttribute = compilation.GetTypeByMetadataName("Hagar.GenerateSerializerAttribute");
            this.fieldIdAttribute = compilation.GetTypeByMetadataName("Hagar.FieldIdAttribute");
        }

        public CompilationUnitSyntax GenerateCode(CancellationToken cancellationToken)
        {
            // Collect metadata from the compilation.
            var serializableTypes = GetSerializableTypes(cancellationToken);

            // Generate code.
            var members = new List<MemberDeclarationSyntax>();
            foreach (var type in serializableTypes)
            {
                // Generate a partial serializer class for each serializable type.
                members.Add(PartialSerializerGenerator.GenerateSerializer(this.compilation, type));
            }

            var namespaceName = "HagarGeneratedCode." + this.compilation.AssemblyName;

            // Generate metadata.
            var metadataClass = MetadataGenerator.GenerateMetadata(this.compilation, serializableTypes);
            members.Add(metadataClass);

            var metadataAttribute = AttributeList()
                .WithTarget(AttributeTargetSpecifier(Token(SyntaxKind.AssemblyKeyword)))
                .WithAttributes(
                    SingletonSeparatedList(
                        Attribute(this.compilation.GetTypeByMetadataName("Hagar.Configuration.MetadataProviderAttribute").ToNameSyntax())
                            .AddArgumentListArguments(AttributeArgument(TypeOfExpression(ParseTypeName($"{namespaceName}.{metadataClass.Identifier.Text}"))))));

            return CompilationUnit()
                .WithAttributeLists(List(new []{GetGeneratedCodeAttribute(), metadataAttribute}))
                .WithMembers(
                    SingletonList<MemberDeclarationSyntax>(
                        NamespaceDeclaration(ParseName(namespaceName))
                        .WithMembers(List(members))
                        .WithUsings(List(new[] {UsingDirective(ParseName("Hagar.Codec"))}))));
        }

        private List<TypeDescription> GetSerializableTypes(CancellationToken cancellationToken)
        {
            var results = new List<TypeDescription>();
            foreach (var syntaxTree in this.compilation.SyntaxTrees)
            {
                var semanticModel = this.compilation.GetSemanticModel(syntaxTree, ignoreAccessibility: false);
                var nodes = syntaxTree.GetRoot(cancellationToken).DescendantNodesAndSelf();
                foreach (var node in nodes)
                {
                    if (!(node is TypeDeclarationSyntax decl)) continue;
                    if (!this.HasGenerateSerializerAttribute(decl, semanticModel)) continue;
                    var typeDescription = this.CreateTypeDescription(semanticModel, decl);
                    results.Add(typeDescription);
                }
            }

            return results;
        }

        private TypeDescription CreateTypeDescription(SemanticModel semanticModel, TypeDeclarationSyntax typeDecl)
        {
            var declared = semanticModel.GetDeclaredSymbol(typeDecl);
            var typeDescription = new TypeDescription(declared, this.GetMembers(declared));
            return typeDescription;
        }

        // Returns descriptions of all fields 
        private IEnumerable<IMemberDescription> GetMembers(INamedTypeSymbol symbol)
        {
            foreach (var member in symbol.GetMembers())
            {
                // Only consider fields and properties.
                if (!(member is IFieldSymbol || member is IPropertySymbol)) continue;

                var fieldIdAttr = member.GetAttributes().SingleOrDefault(attr => attr.AttributeClass.Equals(this.fieldIdAttribute));
                if (fieldIdAttr == null) continue;
                var id = (uint)fieldIdAttr.ConstructorArguments.First().Value;

                if (member is IPropertySymbol prop)
                {
                    if (prop.IsReadOnly || prop.IsWriteOnly)
                    {
#warning add diagnostic: not read/write property.
                        continue;
                    }

                    yield return new PropertyDescription(id, prop);
                }

                if (member is IFieldSymbol field)
                {
                    if (field.IsConst || field.IsReadOnly)
                    {
#warning add diagnostic: readonly field.
                        continue;
                    }
                    
                    yield return new FieldDescription(id, field);
                }
            }
        }

        // Returns true if the type declaration has the [GenerateSerializer] attribute.
        private bool HasGenerateSerializerAttribute(TypeDeclarationSyntax node, SemanticModel model)
        {
            switch (node)
            {
                case ClassDeclarationSyntax classDecl:
                    return HasAttribute(classDecl.AttributeLists);
                case StructDeclarationSyntax structDecl:
                    return HasAttribute(structDecl.AttributeLists);
                default:
                    return false;
            }

            bool HasAttribute(SyntaxList<AttributeListSyntax> attributeLists)
            {
                return attributeLists
                    .SelectMany(list => list.Attributes)
                    .Select(attr => model.GetTypeInfo(attr).ConvertedType)
                    .Any(attrType => attrType.Equals(this.generateSerializerAttribute));
            }
        }

        private static AttributeListSyntax GetGeneratedCodeAttribute()
        {
            var assemblyVersion = typeof(CodeGenerator).Assembly.GetName().Version.ToString();
            var generatedCodeAttribute =
                AttributeList()
                  .AddAttributes(
                      Attribute(ParseName("System.CodeDom.Compiler.GeneratedCodeAttribute"))
                        .AddArgumentListArguments(
                            AttributeArgument("Hagar.CodeGenerator".GetLiteralExpression()),
                            AttributeArgument(assemblyVersion.GetLiteralExpression())))
                  .WithTarget(AttributeTargetSpecifier(Token(SyntaxKind.AssemblyKeyword)));
            return generatedCodeAttribute;

        }

        internal static AttributeSyntax GetGeneratedCodeAttributeSyntax()
        {
            var version = typeof(CodeGenerator).Assembly.GetName().Version.ToString();
            return
                Attribute(ParseName("System.CodeDom.Compiler.GeneratedCodeAttribute"))
                    .AddArgumentListArguments(
                        AttributeArgument(CodeGeneratorName.GetLiteralExpression()),
                        AttributeArgument(version.GetLiteralExpression()));
        }
    }
}