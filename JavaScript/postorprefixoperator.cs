// postorprefixoperator.cs
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

namespace Microsoft.Ajax.Utilities
{
    public enum PostOrPrefix { PostfixDecrement, PostfixIncrement, PrefixDecrement, PrefixIncrement };

    public sealed class PostOrPrefixOperator : UnaryOperator
    {
        public PostOrPrefix Operator { get; set; }

        public PostOrPrefixOperator(Context context, JSParser parser, AstNode operand, JSToken operatorToken, PostOrPrefix postOrPrefixOperator)
            : base(context, parser, operand, operatorToken)
        {
            Operator = postOrPrefixOperator;
        }

        public override PrimitiveType FindPrimitiveType()
        {
            // always returns a number
            return PrimitiveType.Number;
        }

        public override bool IsEquivalentTo(AstNode otherNode)
        {
            var otherOperator = otherNode as PostOrPrefixOperator;
            return otherOperator != null
                && Operator == otherOperator.Operator
                && Operand.IsEquivalentTo(otherOperator.Operand);
        }

        public override void Accept(IVisitor visitor)
        {
            if (visitor != null)
            {
                visitor.Visit(this);
            }
        }

        public override AstNode LeftHandSide
        {
            get
            {
                if (Operator == PostOrPrefix.PostfixDecrement
                  || Operator == PostOrPrefix.PostfixIncrement)
                {
                    // postfix -- the operand is on the left
                    return Operand.LeftHandSide;
                }
                else
                {
                    // prefix operator -- we are on the left
                    return this;
                }
            }
        }

        public override string ToString()
        {
            if (Operator == PostOrPrefix.PrefixIncrement || Operator == PostOrPrefix.PrefixDecrement)
            {
                return OutputVisitor.OperatorString(OperatorToken)
                    + (Operand == null ? "<null>" : Operand.ToString());
            }
            else
            {
                return (Operand == null ? "<null>" : Operand.ToString())
                    + OutputVisitor.OperatorString(OperatorToken);
            }
        }
    }
}