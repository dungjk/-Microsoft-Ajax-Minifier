// Reference.cs
//
// Copyright 2011 Microsoft Corporation
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using System.Collections.Generic;
using System.Text;

namespace Microsoft.Ajax.Utilities.JavaScript
{
    public class Reference
    {
        public string Name { get; private set; }
        public LexicalEnvironment Base { get; set; }
        public Context Context { get; set; }

        public BindingCategory Category
        {
            get
            {
                // try getting the binding object, and if we can, return its type.
                // if we can't, return undefined.
                Binding binding;
                return (Base != null && Base.TryGetBinding(Name, out binding))
                    ? binding.Category
                    : BindingCategory.Undefined;
            }
        }

        public Binding Binding
        {
            get
            {
                // we'll return null if we couldn't resolve the reference
                Binding binding = null;
                if (Base != null)
                {
                    Base.TryGetBinding(Name, out binding);
                }
                return binding;
            }
        }

        public Reference(string name, LexicalEnvironment myBase, Context context)
        {
            Name = name;
            Base = myBase;
            Context = context;
        }

        public void AddReference()
        {
            // make sure it's not an undefined reference before we try to
            // increment the reference count
            var binding = this.Binding;
            if (binding != null)
            {
                ++binding.ReferenceCount;
            }
        }

        public bool IsStrictSpecial
        {
            get
            {
                var objectType = this.Category;
                return objectType == BindingCategory.Arguments
                    || (objectType == BindingCategory.Predefined && string.CompareOrdinal(Name, "eval") == 0);
            }
        }
    }
}
