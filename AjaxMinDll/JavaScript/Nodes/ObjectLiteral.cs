// objectliteral.cs
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

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;

using Microsoft.Ajax.Utilities.JavaScript;
using Microsoft.Ajax.Utilities.JavaScript.Visitors;

namespace Microsoft.Ajax.Utilities.JavaScript.Nodes
{
    public sealed class ObjectLiteral : Expression
    {
        private List<ObjectLiteralField> m_keys;
        public ReadOnlyCollection<ObjectLiteralField> Keys 
        { 
            get { return new ReadOnlyCollection<ObjectLiteralField>(m_keys); }
        }

        private List<AstNode> m_values;
        public ReadOnlyCollection<AstNode> Values
        {
            get { return new ReadOnlyCollection<AstNode>(m_values); }
        }

        // return the length of the keys, since we are ensuring the lengths of the two lists are equal
        public int Count { get { return m_keys.Count; } }

        public ObjectLiteral(Context context, JSParser parser, ObjectLiteralField[] keys, AstNode[] values)
            : base(context, parser)
        {
            // the length of keys and values should be identical.
            // if either is null, or if the lengths don't match, we ignore both!
            if (keys == null || values == null || keys.Length != values.Length)
            {
                // allocate EMPTY lists so we don't have to keep checking for nulls
                m_keys = new List<ObjectLiteralField>();
                m_values = new List<AstNode>();
            }
            else
            {
                // copy the arrays - lengths are the same
                m_keys = new List<ObjectLiteralField>(keys);
                foreach (var key in m_keys)
                {
                    key.Parent = this;
                }

                m_values = new List<AstNode>(values);
                foreach (var item in m_values)
                {
                    item.Parent = this;
                }
            }
        }

        public override void Accept(IVisitor visitor)
        {
            if (visitor != null)
            {
                visitor.Visit(this);
            }
        }

        public override IEnumerable<AstNode> Children
        {
            get
            {
                int count = this.Count;
                for (int ndx = 0; ndx < count; ++ndx)
                {
                    yield return Keys[ndx];
                    yield return Values[ndx];
                }
            }
        }

        public override bool ReplaceChild(AstNode oldNode, AstNode newNode)
        {
            // can't remove one item!
            if (newNode != null)
            {
                // they're both created at the same time, so they should both be non-null.
                // assumption: they should also have the same number of members in them!
                int count = this.Count;
                for (int ndx = 0; ndx < count; ++ndx)
                {
                    if (m_keys[ndx] == oldNode)
                    {
                        if (m_keys[ndx] != null && m_keys[ndx].Parent == this)
                        {
                            m_keys[ndx].Parent = null;
                        }
                        m_keys[ndx] = newNode as ObjectLiteralField;
                        if (m_keys[ndx] != null)
                        {
                            m_keys[ndx].Parent = this;
                        }
                        return true;
                    }
                    if (m_values[ndx] == oldNode)
                    {
                        if (m_values[ndx] != null && m_values[ndx].Parent == this)
                        {
                            m_values[ndx].Parent = null;
                        }
                        m_values[ndx] = newNode;
                        if (m_values[ndx] != null)
                        {
                            m_values[ndx].Parent = this;
                        }
                        return true;
                    }
                }
            }
            return false;
        }

        public void Add(ObjectLiteralField key, AstNode value)
        {
            // can't add a new name/value pair if the name is null
            if (key != null)
            {
                m_keys.Add(key);
                key.Parent = this;

                m_values.Add(value);
                if (value != null)
                {
                    value.Parent = this;
                }
            }
        }

        public bool Remove(ObjectLiteralField key)
        {
            var removed = false;
            for (var ndx = 0; ndx < m_keys.Count; ++ndx)
            {
                if (m_keys[ndx] == key)
                {
                    if (m_keys[ndx] != null && m_keys[ndx].Parent == this)
                    {
                        m_keys[ndx].Parent = null;
                    }
                    m_keys.RemoveAt(ndx);

                    if (m_values[ndx] != null && m_values[ndx].Parent == this)
                    {
                        m_values[ndx].Parent = null;
                    }
                    m_values.RemoveAt(ndx);

                    // bail
                    removed = true;
                    break;
                }
            }

            return removed;
        }

        //public override string ToCode(ToCodeFormat format)
        //{
        //    StringBuilder sb = new StringBuilder();
        //    sb.Append('{');

        //    int count = this.Count;
        //    if (count > 0)
        //    {
        //        Parser.Settings.Indent();
        //        for (int ndx = 0; ndx < count; ++ndx)
        //        {
        //            if (ndx > 0)
        //            {
        //                sb.Append(',');
        //            }

        //            Parser.Settings.NewLine(sb);
        //            sb.Append(Keys[ndx].ToCode());
        //            if (Keys[ndx] is GetterSetter)
        //            {
        //                sb.Append(Values[ndx].ToCode(ToCodeFormat.NoFunction));
        //            }
        //            else
        //            {
        //                // the key is always an identifier, string or numeric literal
        //                sb.Append(':');
        //                sb.Append(Values[ndx].ToCode());
        //            }
        //        }

        //        Parser.Settings.Unindent();
        //        Parser.Settings.NewLine(sb);
        //    }

        //    sb.Append('}');
        //    return sb.ToString();
        //}

        public override string GetFunctionNameGuess(AstNode target)
        {
            // walk the values until we find the target, then return the key
            int count = this.Count;
            for (int ndx = 0; ndx < count; ++ndx)
            {
                if (Values[ndx] == target)
                {
                    // we found it -- return the corresponding key (converted to a string)
                    return Keys[ndx].ToString();
                }
            }
            // if we get this far, we didn't find it
            return string.Empty;
        }
    }
}

