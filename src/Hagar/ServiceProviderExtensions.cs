﻿using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using Hagar.Activators;
using Hagar.Buffers;
using Hagar.Codecs;
using Hagar.Configuration;
using Hagar.Serializers;
using Hagar.Session;
using Hagar.TypeSystem;
using Hagar.WireProtocol;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Hagar
{
    public static class ServiceProviderExtensions
    {
        public static IServiceCollection AddHagar(this IServiceCollection services, Action<SerializerConfiguration> configure = null)
        {
            // Only add the services once.
            var existingContext = GetFromServices<HagarConfigurationContext>(services);
            if (existingContext == null)
            {
                services.Add(HagarConfigurationContext.Descriptor);

                services.AddSingleton<IConfigurationProvider<SerializerConfiguration>, DefaultSerializerConfiguration>();
                services.AddSingleton<IConfigurationProvider<TypeConfiguration>, DefaultTypeConfiguration>();
                services.TryAddSingleton(typeof(IActivator<>), typeof(DefaultActivator<>));
                services.TryAddSingleton(typeof(IConfiguration<>), typeof(ConfigurationHolder<>));
                services.TryAddSingleton<ITypeResolver, CachedTypeResolver>();
                services.TryAddSingleton<CodecProvider>();
                services.TryAddSingleton<IUntypedCodecProvider>(sp => sp.GetRequiredService<CodecProvider>());
                services.TryAddSingleton<ITypedCodecProvider>(sp => sp.GetRequiredService<CodecProvider>());
                services.TryAddSingleton<IPartialSerializerProvider>(sp => sp.GetRequiredService<CodecProvider>());
                services.TryAddScoped(typeof(IFieldCodec<>), typeof(FieldCodecHolder<>));
                services.TryAddScoped(typeof(IPartialSerializer<>), typeof(PartialSerializerHolder<>));
                services.TryAddSingleton<WellKnownTypeCollection>();
                services.TryAddSingleton<TypeCodec>();

                // Session
                services.AddSingleton<SessionPool>();

                // Serializer
                services.AddSingleton(typeof(Serializer<>));
                services.AddSingleton(typeof(Serializer));
            }

            if (configure != null)
            {
                services.AddSingleton<IConfigurationProvider<SerializerConfiguration>>(
                    new DelegateConfigurationProvider<SerializerConfiguration>(configure));
            }

            return services;
        }

        private static T GetFromServices<T>(IServiceCollection services)
        {
            foreach (var service in services )
            {
                if (service.ServiceType == typeof(T)) return (T)service.ImplementationInstance;
            }

            return default(T);
        }

        private class HagarConfigurationContext
        {
            public static readonly ServiceDescriptor Descriptor = new ServiceDescriptor(typeof(HagarConfigurationContext), new HagarConfigurationContext());
        }

        public static IServiceCollection AddSerializers(this IServiceCollection services, Assembly asm)
        {
            var attrs = asm.GetCustomAttributes<MetadataProviderAttribute>();
            foreach (var attr in attrs)
            {
                if (!typeof(IConfigurationProvider<SerializerConfiguration>).IsAssignableFrom(attr.ProviderType)) continue;
                services.AddSingleton(typeof(IConfigurationProvider<SerializerConfiguration>), attr.ProviderType);
            }

            return services;
        }

        private class DelegateConfigurationProvider<TOptions> : IConfigurationProvider<TOptions>
        {
            private readonly Action<TOptions> configure;

            public DelegateConfigurationProvider(Action<TOptions> configure)
            {
                this.configure = configure;
            }

            public void Configure(TOptions configuration) => this.configure(configuration);
        }

        private class FieldCodecHolder<TField> : IFieldCodec<TField>
        {
            private readonly IFieldCodec<TField> codec;

            public FieldCodecHolder(ITypedCodecProvider codecProvider)
            {
                this.codec = codecProvider.GetCodec<TField>();
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void WriteField(Writer writer, SerializerSession session, uint fieldIdDelta, Type expectedType, TField value) => this.codec.WriteField(writer, session, fieldIdDelta, expectedType, value);

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public TField ReadValue(Reader reader, SerializerSession session, Field field) => this.codec.ReadValue(reader, session, field);
        }

        private class PartialSerializerHolder<TField> : IPartialSerializer<TField> where TField : class
        {
            private readonly IPartialSerializer<TField> partialSerializer;
            public PartialSerializerHolder(IPartialSerializerProvider provider)
            {
                this.partialSerializer = provider.GetPartialSerializer<TField>();
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Serialize(Writer writer, SerializerSession session, TField value)
            {
                this.partialSerializer.Serialize(writer, session, value);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Deserialize(Reader reader, SerializerSession session, TField value)
            {
                this.partialSerializer.Deserialize(reader, session, value);
            }
        }
    }
}
