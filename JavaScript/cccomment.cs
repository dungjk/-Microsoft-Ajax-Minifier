// cccomment.cs
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
using System.Text;

namespace Microsoft.Ajax.Utilities
{
    public class ConditionalCompilationComment : AstNode
    {
        public Block Statements { get; private set; }

        public ConditionalCompilationComment(Context context, JSParser parser)
            : base(context, parser)
        {
            Statements = new Block(null, parser);
            Statements.Parent = this;
        }


        internal override bool RequiresSeparator
        {
            get
            {
                return Statements.Count > 0 ? Statements[Statements.Count - 1].RequiresSeparator : true;
            }
        }

        public override void Accept(IVisitor visitor)
        {
            if (visitor != null)
            {
                visitor.Visit(this);
            }
        }

        public void Append(AstNode statement)
        {
            if (statement != null)
            {
                Context.UpdateWith(statement.Context);
                Statements.Append(statement);
            }
        }

        public override IEnumerable<AstNode> Children
        {
            get
            {
                return EnumerateNonNullNodes(Statements);
            }
        }

        public override bool ReplaceChild(AstNode oldNode, AstNode newNode)
        {
            if (Statements == oldNode)
            {
                Statements = ForceToBlock(newNode);
                if (Statements != null) { Statements.Parent = this; }
                return true;
            }
            return false;
        }

        public override string ToCode(ToCodeFormat format)
        {
            StringBuilder sb = new StringBuilder();

            // if there aren't any statements, we don't need this comment.
            // and if there is only one statement and it's a cc_on, we won't need
            // the comment if we've already output a cc_on.
            if (Statements.Count > 1
                || (Statements.Count == 1 && (!(Statements[0] is ConditionalCompilationOn) || !Parser.OutputCCOn
                || !Parser.Settings.IsModificationAllowed(TreeModifications.RemoveUnnecessaryCCOnStatements))))
            {

                // get the statements code - if it's empty, nothing else to do
                var statements = Statements.ToCode(ToCodeFormat.NoBraces);
                if (!string.IsNullOrEmpty(statements))
                {
                    sb.Append("/*");

                    // if it the statements don't already start with an @-sign, then
                    // we'll need to add one now ourselves
                    if (!statements.StartsWith("@", StringComparison.Ordinal))
                    {
                        sb.Append('@');

                        // and if the first character could be an identifier start,
                        // we'll need to add a space, too
                        if (JSScanner.IsValidIdentifierStart(statements[0]))
                        {
                            sb.Append(' ');
                        }
                    }

                    sb.Append(statements);
                    sb.Append("@*/");
                }
            }
            return sb.ToString();
        }
    }
}
