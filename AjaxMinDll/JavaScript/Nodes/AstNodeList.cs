// astlist.cs
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
using System.Globalization;
using System.Text;

using Microsoft.Ajax.Utilities.JavaScript;
using Microsoft.Ajax.Utilities.JavaScript.Visitors;

namespace Microsoft.Ajax.Utilities.JavaScript.Nodes
{

    public sealed class AstNodeList : AstNode
    {
        private List<AstNode> m_list;

        public AstNodeList(Context context, JSParser parser)
            : base(context, parser)
        {
            m_list = new List<AstNode>();
        }

        public override void Accept(IVisitor visitor)
        {
            if (visitor != null)
            {
                visitor.Visit(this);
            }
        }

        public int Count
        {
            get { return m_list.Count; }
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
                    this[ndx] = newNode;
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// an astlist is equivalent to another astlist if they both have the same number of
        /// items, and each item is equivalent to the corresponding item in the other
        /// </summary>
        /// <param name="otherNode"></param>
        /// <returns></returns>
        public override bool IsEquivalentTo(AstNode otherNode)
        {
            bool isEquivalent = false;

            AstNodeList otherList = otherNode as AstNodeList;
            if (otherList != null && m_list.Count == otherList.Count)
            {
                // now assume it's true unless we come across an item that ISN'T
                // equivalent, at which case we'll bail the test.
                isEquivalent = true;
                for (var ndx = 0; ndx < m_list.Count; ++ndx)
                {
                    if (!m_list[ndx].IsEquivalentTo(otherList[ndx]))
                    {
                        isEquivalent = false;
                        break;
                    }
                }
            }

            return isEquivalent;
        }

        internal AstNodeList Append(AstNode astNode)
        {
            astNode.Parent = this;
            m_list.Add(astNode);
            Context.UpdateWith(astNode.Context);
            return this;
        }

        internal void RemoveAt(int position)
        {
            m_list.RemoveAt(position);
        }

        public AstNode this[int index]
        {
            get
            {
                return m_list[index];
            }
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

        public bool IsSingleConstantArgument(string argumentValue)
        {
            if (m_list.Count == 1)
            {
                ConstantWrapper constantWrapper = m_list[0] as ConstantWrapper;
                if (constantWrapper != null 
                    && string.CompareOrdinal(constantWrapper.Value.ToString(), argumentValue) == 0)
                {
                    return true;
                }
            }
            return false;
        }

        public string SingleConstantArgument
        {
            get
            {
                string constantValue = null;
                if (m_list.Count == 1)
                {
                    ConstantWrapper constantWrapper = m_list[0] as ConstantWrapper;
                    if (constantWrapper != null)
                    {
                        constantValue = constantWrapper.ToString();
                    }
                }
                return constantValue;
            }
        }

        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture, "Count: {0}", m_list.Count);
        }
    }
}
