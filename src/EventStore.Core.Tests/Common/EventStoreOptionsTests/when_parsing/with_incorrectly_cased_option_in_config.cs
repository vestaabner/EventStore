using EventStore.Common.Options;
using EventStore.Core.Util;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace EventStore.Core.Tests.Common.EventStoreOptionsTests.when_parsing
{
    [TestFixture]
    public class with_incorrectly_cased_option_in_config
    {
        [Test]
        public void should_be_able_to_parse_the_option_ignoring_casing()
        {
            var args = new string[] { "-config", "TestConfigs/test_config_with_incorrectly_cased_option.yaml" };
            var options = EventStoreOptions.Parse<TestArgs>(args, Opts.EnvPrefix);
            Assert.AreEqual("~/gesLogs", options.Log);
            Assert.AreEqual(ProjectionType.All, options.RunProjections);
        }
    }
}
