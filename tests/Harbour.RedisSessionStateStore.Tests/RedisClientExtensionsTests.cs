using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Xunit;
using ServiceStack.Redis;
using ServiceStack.Text;

namespace Harbour.RedisSessionStateStore.Tests
{
    public class RedisClientExtensionsTests : RedisTest
    {
        [Fact]
        public void SetRangeInHashRaw()
        {
            redis.SetRangeInHashRaw("abc:123", new Dictionary<string, byte[]>()
            {
                { "a", "abc123".ToUtf8Bytes() },
                { "b", "1".ToUtf8Bytes() },
                { "c", "".ToUtf8Bytes() }
            });

            var result = redis.GetAllEntriesFromHash("abc:123");
            Assert.Equal(3, result.Count);
            Assert.Equal("abc123", result["a"]);
            Assert.Equal("1", result["b"]);
            Assert.Equal("", result["c"]);
        }

        [Fact]
        public void GetAllEntriesFromHashRaw()
        {
            redis.SetRangeInHashRaw("abc:123", new Dictionary<string, byte[]>()
            {
                { "a", new byte[] { 1, 2, 3, 4 } },
                { "b", new byte[] { 1 } },
                { "c", new byte[0] },
            });

            var result = redis.GetAllEntriesFromHashRaw("abc:123");
            Assert.Equal(3, result.Count);
            Assert.Equal(new byte[] { 1, 2, 3, 4 }, result["a"]);
            Assert.Equal(new byte[] { 1 }, result["b"]);
            Assert.Equal(new byte[0], result["c"]);
        }

        [Fact]
        public void GetValueFromHashRaw()
        {
            redis.SetRangeInHashRaw("abc:123", new Dictionary<string, byte[]>()
            {
                { "a", new byte[]{ 1, 2, 3, 4 } }
            });

            var result = redis.GetValueFromHashRaw("abc:123", "a");
            Assert.Equal(new byte[] { 1, 2, 3, 4 }, result);
        }

        [Fact]
        public void SetEntryInHashIfNotExists()
        {
            Assert.True(redis.SetEntryInHashIfNotExists("abc:123", "a", new byte[] { 1, 2, 3, 4 }));
            Assert.Equal(new byte[]{ 1, 2, 3, 4 }, redis.GetValueFromHashRaw("abc:123", "a"));
            Assert.False(redis.SetEntryInHashIfNotExists("abc:123", "a", new byte[] { 4, 5, 6, 7 }));
            Assert.Equal(new byte[] { 1, 2, 3, 4 }, redis.GetValueFromHashRaw("abc:123", "a"));
        }
    }
}
