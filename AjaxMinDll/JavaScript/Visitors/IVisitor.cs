// IVisitor.cs
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

using Microsoft.Ajax.Utilities.JavaScript.Nodes;

namespace Microsoft.Ajax.Utilities.JavaScript.Visitors
{
    public interface IVisitor
    {
        void Visit(ArrayLiteral node);
        void Visit(AspNetBlockNode node);
        void Visit(AstNodeList node);
        void Visit(BinaryOperator node);
        void Visit(Block node);
        void Visit(BreakStatement node);
        void Visit(CallNode node);
        void Visit(ConditionalCompilationComment node);
        void Visit(ConditionalCompilationElse node);
        void Visit(ConditionalCompilationElseIf node);
        void Visit(ConditionalCompilationEnd node);
        void Visit(ConditionalCompilationIf node);
        void Visit(ConditionalCompilationOn node);
        void Visit(ConditionalCompilationSet node);
        void Visit(Conditional node);
        void Visit(ConstantWrapper node);
        void Visit(ConstantWrapperPP node);
        void Visit(ContinueStatement node);
        void Visit(DebuggerStatement node);
        void Visit(DeleteNode node);
        void Visit(DoWhileStatement node);
        void Visit(ForEachStatement node);
        void Visit(ForStatement node);
        void Visit(FunctionObject node);
        void Visit(GetterSetter node);
        void Visit(IfStatement node);
        void Visit(ImportantComment node);
        void Visit(LabeledStatement node);
        void Visit(Lookup node);
        void Visit(Member node);
        void Visit(NumericUnary node);
        void Visit(ObjectLiteral node);
        void Visit(ObjectLiteralField node);
        void Visit(PostOrPrefixOperator node);
        void Visit(RegExpLiteral node);
        void Visit(ReturnStatement node);
        void Visit(SwitchStatement node);
        void Visit(SwitchCase node);
        void Visit(ThisLiteral node);
        void Visit(ThrowStatement node);
        void Visit(TryStatement node);
        void Visit(TypeOfNode node);
        void Visit(VarStatement node);
        void Visit(VariableDeclaration node);
        void Visit(VoidNode node);
        void Visit(WhileStatement node);
        void Visit(WithStatement node);
    }
}
