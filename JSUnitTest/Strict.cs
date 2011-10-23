// Strict.cs
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

using Microsoft.Ajax.Utilities;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace JSUnitTest
{
    /// <summary>
    /// Summary description for Strict
    /// </summary>
    [TestClass]
    public class Strict
    {
        [TestMethod]
        public void EvalArgsAssign()
        {
            TestHelper.Instance.RunErrorTest("-rename:none", 
                JSError.StrictModeInvalidAssign, 
                JSError.StrictModeInvalidAssign,
                JSError.StrictModeInvalidPreOrPost, 
                JSError.StrictModeInvalidPreOrPost, 
                JSError.StrictModeInvalidPreOrPost, 
                JSError.StrictModeInvalidPreOrPost);
        }

        [TestMethod]
        public void InvalidVarName()
        {
            TestHelper.Instance.RunErrorTest("-rename:none",
                JSError.StrictModeVariableName,
                JSError.StrictModeVariableName,
                JSError.StrictModeVariableName,
                JSError.StrictModeVariableName,
                JSError.StrictModeArgumentName,
                JSError.StrictModeArgumentName,
                JSError.StrictModeFunctionName,
                JSError.StrictModeFunctionName,
                JSError.UndeclaredFunction);
        }

        [TestMethod]
        public void DupArg()
        {
            TestHelper.Instance.RunErrorTest("-rename:none",
                JSError.StrictModeDuplicateArgument,
                JSError.DuplicateName);
        }

        [TestMethod]
        public void With()
        {
            TestHelper.Instance.RunErrorTest("-rename:none", JSError.StrictModeNoWith);
        }

        [TestMethod]
        public void DupProperty()
        {
            TestHelper.Instance.RunErrorTest("-rename:none",
                JSError.StrictModeDuplicateProperty,
                JSError.StrictModeDuplicateProperty,
                JSError.StrictModeDuplicateProperty,
                JSError.StrictModeDuplicateProperty,
                JSError.StrictModeDuplicateProperty,
                JSError.StrictModeDuplicateProperty,
                JSError.StrictModeDuplicateProperty);
        }

        [TestMethod]
        public void InvalidDelete()
        {
            TestHelper.Instance.RunErrorTest("-rename:none",
                JSError.StrictModeInvalidDelete,
                JSError.StrictModeInvalidDelete,
                JSError.StrictModeInvalidDelete);
        }

        [TestMethod]
        public void FuncDeclLoc()
        {
            // no errors! not in strict mode
            TestHelper.Instance.RunErrorTest("-rename:none");
        }

        [TestMethod]
        public void FuncDeclStrict()
        {
            // error because it's in strict mode
            TestHelper.Instance.RunErrorTest("-rename:none", JSError.MisplacedFunctionDeclaration);
        }

        [TestMethod]
        public void ExtraUseStrict()
        {
            // error because it's in strict mode
            TestHelper.Instance.RunTest();
        }

        [TestMethod]
        public void StrictSwitchOn()
        {
            // error because it's in strict mode
            TestHelper.Instance.RunTest("-strict -xml");
        }
    }
}
