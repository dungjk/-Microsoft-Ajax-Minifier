// var.cs
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
using System.Text;

using Microsoft.Ajax.Utilities.JavaScript;
using Microsoft.Ajax.Utilities.JavaScript.Visitors;

namespace Microsoft.Ajax.Utilities.JavaScript.Nodes
{
    /// <summary>
    /// Summary description for variablestatement.
    /// </summary>
    public sealed class VarStatement : AstNode
    {
        private List<VariableDeclaration> m_list;

        public int Count
        {
            get { return m_list.Count; }
        }

        public VariableDeclaration this[int index]
        {
            get { return m_list[index]; }
            set
            {
                if (value != m_list[index])
                {
                    if (m_list[index] != null && m_list[index].Parent == this)
                    {
                        m_list[index].Parent = null;
                    }

                    if (value == null)
                    {
                        // just remove it
                        m_list.RemoveAt(index);
                    }
                    else
                    {
                        m_list[index] = value;
                        value.Parent = this;
                    }
                }
            }
        }

        public VarStatement(Context context, JSParser parser)
            : base(context, parser)
        {
            m_list = new List<VariableDeclaration>();
        }

        public bool HasAnyInitializers
        {
            get
            {
                var hasAtLeastOne = false;
                foreach (var vardecl in m_list)
                {
                    if (vardecl.Initializer != null)
                    {
                        hasAtLeastOne = true;
                        break;
                    }
                }
                return hasAtLeastOne;
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
                // use ToArray to get a list of the children AT THIS POINT IN TIME
                return EnumerateNonNullNodes(m_list.ToArray());
            }
        }

        public override bool ReplaceChild(AstNode oldNode, AstNode newNode)
        {
            for (int ndx = 0; ndx < m_list.Count; ++ndx)
            {
                if (m_list[ndx] == oldNode)
                {
                    this[ndx] = newNode as VariableDeclaration;
                    return true;
                }
            }
            return false;
        }

        internal void Append(AstNode elem)
        {
            VariableDeclaration decl = elem as VariableDeclaration;
            if (decl != null)
            {
                // first check the list for existing instances of this name.
                // if there are no duplicates (indicated by returning true), add it to the list.
                // if there is a dup (indicated by returning false) then that dup
                // has an initializer, and we DON'T want to add this new one if it doesn't
                // have it's own initializer.
                if (HandleDuplicates(decl.Identifier)
                    || decl.Initializer != null)
                {
                    // set the parent and add it to the list
                    decl.Parent = this;
                    m_list.Add(decl);
                }
            }
            else
            {
                VarStatement otherVar = elem as VarStatement;
                if (otherVar != null)
                {
                    for (int ndx = 0; ndx < otherVar.m_list.Count; ++ndx)
                    {
                        Append(otherVar.m_list[ndx]);
                    }
                }
            }
        }

        internal void InsertAt(int index, AstNode elem)
        {
            VariableDeclaration decl = elem as VariableDeclaration;
            if (decl != null)
            {
                // first check the list for existing instances of this name.
                // if there are no duplicates (indicated by returning true), add it to the list.
                // if there is a dup (indicated by returning false) then that dup
                // has an initializer, and we DON'T want to add this new one if it doesn't
                // have it's own initializer.
                if (HandleDuplicates(decl.Identifier)
                    || decl.Initializer != null)
                {
                    // set the parent and add it to the list
                    decl.Parent = this;
                    m_list.Insert(index, decl);
                }
            }
            else
            {
                VarStatement otherVar = elem as VarStatement;
                if (otherVar != null)
                {
                    // walk the source backwards so they end up in the right order
                    for (int ndx = otherVar.m_list.Count - 1; ndx >= 0; --ndx)
                    {
                        InsertAt(index, otherVar.m_list[ndx]);
                    }
                }
            }
        }

        private bool HandleDuplicates(string name)
        {
            var hasInitializer = true;
            // walk backwards because we'll be removing items from the list
            for (var ndx = m_list.Count - 1; ndx >= 0 ; --ndx)
            {
                VariableDeclaration varDecl = m_list[ndx];

                // if the name is a match...
                if (string.CompareOrdinal(varDecl.Identifier, name) == 0)
                {
                    // check the initializer. If there is no initializer, then
                    // we want to remove it because we'll be adding a new one.
                    // but if there is an initializer, keep it but return false
                    // to indicate that there is still a duplicate in the list, 
                    // and that dup has an initializer.
                    if (varDecl.Initializer != null)
                    {
                        hasInitializer = false;
                    }
                    else
                    {
                        if (m_list[ndx] != null && m_list[ndx].Parent == this)
                        {
                            m_list[ndx].Parent = null;
                        }
                        m_list.RemoveAt(ndx);
                    }
                }
            }

            return hasInitializer;
        }

        public void RemoveAt(int index)
        {
            if (0 <= index & index < m_list.Count)
            {
                if (m_list[index] != null && m_list[index].Parent == this)
                {
                    m_list[index].Parent = null;
                }
                m_list.RemoveAt(index);
            }
        }

        public bool Contains(string name)
        {
            // look at each vardecl in our list
            foreach(var varDecl in m_list)
            {
                // if it matches the target name exactly...
                if (string.CompareOrdinal(varDecl.Identifier, name) == 0)
                {
                    // ...we found a match
                    return true;
                }
            }
            // if we get here, we didn't find any matches
            return false;
        }

        //public override string ToCode(ToCodeFormat format)
        //{
        //    StringBuilder sb = new StringBuilder();
        //    sb.Append("var ");
        //    Parser.Settings.Indent();

        //    bool first = true;
        //    for (int ndx = 0; ndx < m_list.Count; ++ndx)
        //    {
        //        VariableDeclaration vdecl = m_list[ndx];
        //        if (vdecl != null)
        //        {
        //            if (!first)
        //            {
        //                sb.Append(',');
        //                Parser.Settings.NewLine(sb);
        //            }
        //            sb.Append(vdecl.ToCode());
        //            first = false;
        //        }
        //    }
        //    Parser.Settings.Unindent();
        //    return sb.ToString();
        //}
    }
}
