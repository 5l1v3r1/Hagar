﻿using System;
using System.Collections.Generic;

namespace Hagar.Configuration
{
    public class SerializerConfiguration
    {
        public HashSet<Type> FieldCodecs { get; } = new HashSet<Type>();

        public HashSet<Type> PartialSerializers { get; } = new HashSet<Type>();
    }

    public class TypeConfiguration
    {
        public Dictionary<uint, Type> WellKnownTypes { get; } = new Dictionary<uint, Type>();
    }
}