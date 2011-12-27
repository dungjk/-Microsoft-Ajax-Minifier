using System;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CssUnitTest
{
    /// <summary>
    /// Summary description for Values
    /// </summary>
    [TestClass]
    public class Values
    {
        [TestMethod]
        public void Calc()
        {
            TestHelper.Instance.RunTest();
        }

        [TestMethod]
        public void Cycle()
        {
            TestHelper.Instance.RunTest();
        }

        [TestMethod]
        public void Attr()
        {
            TestHelper.Instance.RunTest();
        }
    }
}
