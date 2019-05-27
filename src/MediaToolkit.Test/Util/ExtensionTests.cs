using NUnit.Framework;
using System;
using System.Linq;
using System.Collections.Generic;

namespace MediaToolkit.Test.Util
{
    [TestFixture]
    public class ExtensionTests
    {
        public class ForEach
        {
            [SetUp]
            public void SetUp() => CollectionUnderTest = new[] { "Foo", "Bar" };

            public IEnumerable<string> CollectionUnderTest;

            [Test]
            public void Will_Iterate_Through_EachItem_InCollection()
            {
                int expectedIterations = 2;
                int iterations = 0;

                CollectionUnderTest
                    .ToList()
                    .ForEach(item => iterations++);

                Assert.That(iterations == expectedIterations);
            }

            [Test]
            public void When_ActionIsNull_Throw_ArgumentNullException()
            {
                Type expectedException = typeof(ArgumentNullException);

                void codeUnderTest() => CollectionUnderTest
                    .ToList()
                    .ForEach(null);

                Assert.Throws(expectedException, codeUnderTest);
            }
        }
    }
}
