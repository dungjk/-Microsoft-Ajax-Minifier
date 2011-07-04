// JSObject.cs
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

using Microsoft.Ajax.Utilities.JavaScript.Nodes;

namespace Microsoft.Ajax.Utilities.JavaScript
{
    public enum BindingCategory
    {
        Normal = 0,
        Argument,
        Arguments,
        CatchArgument,
        RenamingPlaceholder,
        Predefined,
        NamedFunctionExpression,
        Property,
        Undefined = -1,
    }

    public class Binding
    {
        // original name of the binding
        public string Name { get; set; }

        // optional alternate name
        public string AlternateName { get; set; }

        // optional "value" for the binding
        public AstNode Value { get; set; }

        // optional ambiguous "value" when there may be cross-browser ambiguity
        public AstNode AmbiguousValue { get; set; }

        // context for where this binding is originally defined
        public Context DefinitionContext { get; set; }

        // a reference to which this binding is related
        public Reference Linked { get; set; }

        public bool CanDelete { get; set; }
        public bool IsImmutable { get; set; }
        public bool IsInitialized { get; set; }
        public bool IsConfigurable { get; set; }
        public bool IsWritable { get; set; }
        public bool IsEnumerable { get; set; }

        // whether this binding comes from source, or is generated during minification
        public bool IsGenerated { get; set; }

        // binding category is extra information regarding what the binding is used for
        public BindingCategory Category { get; set; }

        // rough relative position index of the definition within the source code
        public int Position { get; set; }

        // true if this binding is a candidate for renaming; false if the field should not be renamed
        public bool CanRename { get; set; }

        // number of times the binding is referenced
        // TODO: do we need seperate setting/getting counts?
        public int ReferenceCount { get; set; }
        public bool IsReferenced { get { return ReferenceCount > 0; } }

        public Binding(string name)
        {
            // set the name and the default value is undefined
            Name = name;
            Value = null;

            // by default references can be deleted, are not immutable, and not initialized
            CanDelete = true;
            IsConfigurable = true;
            IsWritable = true;
            CanRename = true;
        }

        public override string ToString()
        {
            // return the minified name if present, otherwise the original name
            return string.IsNullOrEmpty(AlternateName) ? Name : AlternateName;
        }
    }
}
