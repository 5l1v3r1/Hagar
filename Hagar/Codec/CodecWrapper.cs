using System;
using Hagar.Buffers;
using Hagar.Serializer;
using Hagar.Session;
using Hagar.WireProtocol;

namespace Hagar.Codec
{
    public static class CodecWrapper
    {
        public static IFieldCodec<object> CreateUntypedFromTyped<TField, TCodec>(TCodec typedCodec) where TCodec : IFieldCodec<TField>
        {
            return new TypedCodecWrapper<TField, TCodec>(typedCodec);
        }

        public static IFieldCodec<TField> CreatedTypedFromUntyped<TField>(IFieldCodec<object> untypedCodec)
        {
            return new UntypedCodecWrapper<TField>(untypedCodec);
        }

        private class TypedCodecWrapper<TField, TCodec> : IFieldCodec<object>, IWrappedCodec where TCodec : IFieldCodec<TField>
        {
            private readonly TCodec codec;

            public TypedCodecWrapper(TCodec codec)
            {
                this.codec = codec;
            }

            public void WriteField(Writer writer, SerializerSession session, uint fieldIdDelta, Type expectedType, object value)
            {
                this.codec.WriteField(writer, session, fieldIdDelta, expectedType, (TField)value);
            }

            public object ReadValue(Reader reader, SerializerSession session, Field field)
            {
                return this.codec.ReadValue(reader, session, field);
            }

            public object InnerCodec => this.codec;
        }

        private class UntypedCodecWrapper<TField> : IWrappedCodec, IFieldCodec<TField>
        {
            private readonly IFieldCodec<object> codec;

            public UntypedCodecWrapper(IFieldCodec<object> codec)
            {
                this.codec = codec;
            }

            public object InnerCodec => this.codec;
            public void WriteField(Writer writer, SerializerSession session, uint fieldIdDelta, Type expectedType, TField value)
            {
                this.codec.WriteField(writer, session, fieldIdDelta, expectedType, value);
            }

            public TField ReadValue(Reader reader, SerializerSession session, Field field)
            {
                return (TField)this.codec.ReadValue(reader, session, field);
            }
        }
    }
}