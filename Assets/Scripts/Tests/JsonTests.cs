using System;
using NUnit.Framework;
using VoidRunner.Content;

namespace VoidRunner.Tests
{
    public sealed class JsonTests
    {
        [Test]
        public void ParsesNestedObjectsAndArrays()
        {
            var v = Json.Parse("{ \"a\": [1, 2, 3], \"b\": { \"c\": true, \"d\": \"hi\" } }");
            Assert.IsTrue(v.IsObject);
            Assert.AreEqual(3, v["a"].AsArray.Count);
            Assert.AreEqual(2.0, v["a"].AsArray[1].AsNumber);
            Assert.IsTrue(v["b"]["c"].AsBool);
            Assert.AreEqual("hi", v["b"]["d"].AsString);
        }

        [Test]
        public void ParsesTopLevelArray()
        {
            var v = Json.Parse("[10, 20, 30]");
            Assert.IsTrue(v.IsArray);
            Assert.AreEqual(30.0, v.AsArray[2].AsNumber);
        }

        [Test]
        public void HandlesEscapesAndUnicode()
        {
            var v = Json.Parse("\"line1\\nline2\\u0041\"");
            Assert.AreEqual("line1\nline2A", v.AsString);
        }

        [Test]
        public void SupportsLineAndBlockComments()
        {
            var v = Json.Parse("{ // a comment\n \"x\": 1 /* inline */, \"y\": 2 }");
            Assert.AreEqual(1, v.GetInt("x"));
            Assert.AreEqual(2, v.GetInt("y"));
        }

        [Test]
        public void ParsesNegativeAndExponentNumbers()
        {
            var v = Json.Parse("[-3.5, 2e3, 1.5e-2]");
            Assert.AreEqual(-3.5, v.AsArray[0].AsNumber, 1e-9);
            Assert.AreEqual(2000.0, v.AsArray[1].AsNumber, 1e-9);
            Assert.AreEqual(0.015, v.AsArray[2].AsNumber, 1e-9);
        }

        [Test]
        public void ReportsErrorWithLineAndColumn()
        {
            var ex = Assert.Throws<JsonParseException>(() => Json.Parse("{ \"a\": }"));
            StringAssert.Contains("line", ex.Message);
            StringAssert.Contains("column", ex.Message);
        }

        [Test]
        public void RejectsTrailingGarbage()
        {
            Assert.Throws<JsonParseException>(() => Json.Parse("{} extra"));
        }

        [Test]
        public void RejectsUnterminatedString()
        {
            Assert.Throws<JsonParseException>(() => Json.Parse("\"abc"));
        }

        [Test]
        public void MissingKeyAccessorsReturnDefaults()
        {
            var v = Json.Parse("{ \"present\": 5 }");
            Assert.AreEqual(5, v.GetInt("present"));
            Assert.AreEqual(42, v.GetInt("absent", 42));
            Assert.AreEqual("fallback", v.GetString("absent", "fallback"));
            Assert.IsTrue(v.GetBool("absent", true));
        }

        [Test]
        public void EscapeRoundTripsThroughParser()
        {
            string original = "quote\" back\\slash\ttab\nnewline";
            string encoded = Json.Escape(original);
            var parsed = Json.Parse(encoded);
            Assert.AreEqual(original, parsed.AsString);
        }
    }
}
