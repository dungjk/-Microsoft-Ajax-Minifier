// functionscope.cs
//
// Copyright 2010 Microsoft Corporation
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
using System.Reflection;

namespace Microsoft.Ajax.Utilities
{
    public sealed class FunctionScope : ActivationObject
    {
        public FunctionObject FunctionObject { get; set; }

        private HashSet<ActivationObject> m_refScopes;

        internal FunctionScope(ActivationObject parent, bool isExpression, JSParser parser)
            : base(parent, parser)
        {
            m_refScopes = new HashSet<ActivationObject>();
            if (isExpression)
            {
                // parent scopes automatically reference enclosed function expressions
                AddReference(Parent);
            }
        }

        internal bool IsArgumentTrimmable(JSVariableField argumentField)
        {
            return FunctionObject.IsArgumentTrimmable(argumentField);
        }

        public override JSVariableField CreateField(string name, object value, FieldAttributes attributes)
        {
            return new JSVariableField(FieldType.Local, name, attributes, value);
        }

        internal void AddReference(ActivationObject scope)
        {
            // we don't want to include block scopes or with scopes -- they are really
            // contained within their parents
            while (scope != null && scope is BlockScope)
            {
                scope = scope.Parent;
            }

            if (scope != null)
            {
                // add the scope to the hash
                m_refScopes.Add(scope);
            }
        }
    }
}
