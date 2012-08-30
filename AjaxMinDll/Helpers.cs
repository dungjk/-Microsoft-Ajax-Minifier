// Helpers.cs
//
// Copyright 2012 Microsoft Corporation
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

namespace Microsoft.Ajax.Utilities
{
#if NET_20
    using System.Collections;

    // these are a few of the many useful delegates defined in .NET 3.5 and higher
    public delegate TResult Func<in T1, out TResult>(T1 arg1);
    public delegate TResult Func<in T1, in T2, out TResult>(T1 arg1, T2 arg2);

    /// <summary>
    /// class HashSet doesn't really do it justice, but it will fit the bill for what we need of it
    /// </summary>
    /// <typeparam name="T"></typeparam>
    internal class HashSet<T>
    {
        private Hashtable m_table;

        public HashSet()
        {
            m_table = new Hashtable();
        }

        public bool Add(T item)
        {
            var added = !m_table.ContainsKey(item);
            if (added)
            {
                m_table.Add(item, null);
            }

            return added;
        }
    }

#endif

}
