using System;
using System.Runtime.ExceptionServices;
using Hagar.Buffers;
using Hagar.Serializer;
using Hagar.Session;
using Hagar.Utilities;
using Hagar.WireProtocol;

namespace Hagar.Codec
{
    public class TypeSerializerCodec : IFieldCodec<Type>
    {
        private readonly ICodecProvider codecProvider;
        private static readonly Type SchemaTypeType = typeof(SchemaType);
        private static readonly Type TypeType = typeof(Type);
        private static readonly Type ByteArrayType = typeof(byte[]);
        private static readonly Type UIntType = typeof(uint);

        public TypeSerializerCodec(ICodecProvider codecProvider)
        {
            this.codecProvider = codecProvider;
        }

        public void WriteField(Writer writer, SerializerSession session, uint fieldIdDelta, Type expectedType, Type value)
        {
            if (ReferenceCodec.TryWriteReferenceField(writer, session, fieldIdDelta, expectedType, value)) return;
            writer.WriteFieldHeader(session, fieldIdDelta, expectedType, TypeType, WireType.TagDelimited);
            var (schemaType, id) = GetSchemaType(session, value);

            // Write the encoding type
            writer.WriteFieldHeader(session, 0, SchemaTypeType, SchemaTypeType, WireType.VarInt);
            writer.WriteVarInt((uint) schemaType);

            if (schemaType == SchemaType.Encoded)
            {
                // If the type is encoded, write the length-prefixed bytes.
                writer.WriteFieldHeader(session, 1, ByteArrayType, ByteArrayType, WireType.LengthPrefixed);
                session.TypeCodec.Write(writer, value);
            }
            else
            {
                // If the type is referenced or well-known, write it as a varint.
                writer.WriteFieldHeader(session, 2, UIntType, UIntType, WireType.VarInt);
                writer.WriteVarInt((uint) id);
            }

            writer.WriteEndObject();
        }

        public Type ReadValue(Reader reader, SerializerSession session, Field field)
        {
            if (field.WireType == WireType.Reference) return ReferenceCodec.ReadReference<Type>(reader, session, field, this.codecProvider);

            uint fieldId = 0;
            var schemaType = default(SchemaType);
            uint id = 0;
            Type result = null;
            while (true)
            {
                var header = reader.ReadFieldHeader(session);
                if (header.IsEndBaseOrEndObject) break;
                fieldId += header.FieldIdDelta;
                switch (fieldId)
                {
                    case 0:
                        schemaType = (SchemaType) reader.ReadVarUInt32();
                        break;
                    case 1:
                        result = session.TypeCodec.Read(reader);
                        break;
                    case 2:
                        id = reader.ReadVarUInt32();
                        break;
                }
            }

            switch (schemaType)
            {
                case SchemaType.Referenced:
                    if (session.ReferencedTypes.TryGetReferencedType(id, out result)) return result;
                    return ThrowUnknownReferencedType(id);
                case SchemaType.WellKnown:
                    if (session.WellKnownTypes.TryGetWellKnownType(id, out result)) return result;
                    return ThrowUnknownWellKnownType(id);
                case SchemaType.Encoded:
                    if (result != null) return result;
                    return ThrowMissingType();
                default:
                    return ThrowInvalidSchemaType(schemaType);
            }
        }

        private static (SchemaType, uint) GetSchemaType(SerializerSession session, Type actualType)
        {
            if (session.WellKnownTypes.TryGetWellKnownTypeId(actualType, out uint typeId))
            {
                return (SchemaType.WellKnown, typeId);
            }

            if (session.ReferencedTypes.TryGetTypeReference(actualType, out uint reference))
            {
                return (SchemaType.Referenced, reference);
            }

            return (SchemaType.Encoded, 0);
        }

        private static Type ThrowInvalidSchemaType(SchemaType schemaType) => throw new NotSupportedException(
            $"SchemaType {schemaType} is not supported by {nameof(TypeSerializerCodec)}.");

        private static Type ThrowUnknownReferencedType(uint id) => throw new UnknownReferencedTypeException(id);
        private static Type ThrowUnknownWellKnownType(uint id) => throw new UnknownWellKnownTypeException(id);
        private static Type ThrowMissingType() => throw new TypeMissingException();
    }
}