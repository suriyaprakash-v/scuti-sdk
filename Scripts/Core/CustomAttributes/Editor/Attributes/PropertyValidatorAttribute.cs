﻿using System;

namespace Scuti.Editor
{
    public class PropertyValidatorAttribute : BaseAttribute
    {
        public PropertyValidatorAttribute(Type targetAttributeType) : base(targetAttributeType)
        {
        }
    }
}
