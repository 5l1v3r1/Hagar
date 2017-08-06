using System;
using System.Collections.Generic;
using Hagar.Codec;
using Hagar.Session;
using Hagar.Utilities;
using Hagar.WireProtocol;

namespace Hagar.Serializer
{
    /// <summary>
    /// Serializer for types which are abstract and therefore cannot be instantiated themselves, such as abstract classes and interface types.
    /// </summary>
    /// <typeparam name="TField"></typeparam>
    public class AbstractTypeSerializer<TField> : IFieldCodec<TField> where TField : class
    {
        private readonly ISerializerCatalog serializerCatalog;

        public AbstractTypeSerializer(ISerializerCatalog serializerCatalog)
        {
            this.serializerCatalog = serializerCatalog;
        }

        public void WriteField(Writer writer, SerializerSession session, uint fieldId, Type expectedType, TField value)
        {
            if (ReferenceCodec.TryWriteReferenceField(writer, session, fieldId, expectedType, value)) return;
            var fieldType = value.GetType();
            var specificSerializer = this.serializerCatalog.GetSerializer(fieldType);
            if (specificSerializer != null)
            {
                specificSerializer.WriteField(writer, session, fieldId, expectedType, value);
            }
            else
            {
                ThrowSerializerNotFound(fieldType);
            }
        }

        public TField ReadValue(Reader reader, SerializerSession session, Field field)
        {
            if (field.WireType == WireType.Reference) return ReferenceCodec.ReadReference<TField>(reader, session);
            var fieldType = field.FieldType;
            if (fieldType == null) ThrowMissingFieldType();

            var specificSerializer = this.serializerCatalog.GetSerializer(fieldType);
            if (specificSerializer != null)
            {
                return (TField)specificSerializer.ReadValue(reader, session, field);
            }

            ThrowSerializerNotFound(fieldType);
            return null;
        }

        private static void ThrowSerializerNotFound(Type type)
        {
            throw new KeyNotFoundException($"Could not find a serializer of type {type}.");
        }
        
        private static void ThrowMissingFieldType()
        {
            throw new FieldTypeMissingException(typeof(TField));
        }
    }
}