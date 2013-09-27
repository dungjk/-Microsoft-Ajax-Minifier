// JSEncoding.cs
//
// Copyright 2013 Microsoft Corporation
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
using System.Diagnostics;

using Microsoft.Ajax.Utilities;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DllUnitTest
{
    /// <summary>
    /// Summary description for CssErrorStrings
    /// </summary>
    [TestClass]
    public class JSEncoding
    {
        [TestMethod]
        public void UnicodeEscapedHighSurrogate()
        {
            var minifier = new Minifier();
            var source = "var str = '\\ud83d\ude80';";
            var minified = minifier.MinifyJavaScript(source);
            foreach (var error in minifier.ErrorList)
            {
                Trace.WriteLine(error.ToString());
            }

            Assert.AreEqual("var str=\"🚀\"", minified);
            Assert.AreEqual(0, minifier.ErrorList.Count);
        }

        [TestMethod]
        public void UnicodeEscapedLowSurrogate()
        {
            var minifier = new Minifier();
            var source = "var str = '\ud83d\\ude80';";
            var minified = minifier.MinifyJavaScript(source);
            foreach (var error in minifier.ErrorList)
            {
                Trace.WriteLine(error.ToString());
            }

            Assert.AreEqual("var str=\"🚀\"", minified);
            Assert.AreEqual(0, minifier.ErrorList.Count);
        }

        [TestMethod]
        public void SurrogatePairEscapedIdentifier()
        {
            var minifier = new Minifier();
            var source = "var \\ud840\\udc2f = 'foo';";
            var minified = minifier.MinifyJavaScript(source);
            foreach (var error in minifier.ErrorList)
            {
                Trace.WriteLine(error.ToString());
            }

            Assert.AreEqual("var 𠀯=\"foo\"", minified);
            Assert.AreEqual(0, minifier.ErrorList.Count);
        }

        [TestMethod]
        public void SurrogatePairIdentifier()
        {
            var minifier = new Minifier();
            var source = "var \ud840\udc2d = 'foo';";
            var minified = minifier.MinifyJavaScript(source);
            foreach (var error in minifier.ErrorList)
            {
                Trace.WriteLine(error.ToString());
            }

            Assert.AreEqual("var 𠀭=\"foo\"", minified);
            Assert.AreEqual(0, minifier.ErrorList.Count);
        }
    }
}
