using System;

namespace Hagar
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Enum)]
    public sealed class GenerateSerializerAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Interface, AllowMultiple = true)]
    public sealed class GenerateMethodSerializersAttribute : Attribute
    {
        public GenerateMethodSerializersAttribute(Type proxyBase, bool isExtension = false)
        {
            this.ProxyBase = proxyBase;
            this.IsExtension = isExtension;
        }

        public Type ProxyBase { get; }
        public bool IsExtension { get; }
    }

    [AttributeUsage(
        AttributeTargets.Field
        | AttributeTargets.Property
        | AttributeTargets.Class
        | AttributeTargets.Struct
        | AttributeTargets.Enum
        | AttributeTargets.Method)]
    public sealed class IdAttribute : Attribute
    {
        public IdAttribute(ushort id)
        {
            this.Id = id;
        }

        public ushort Id { get; }
    }
}
