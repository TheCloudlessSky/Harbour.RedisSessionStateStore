using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Xunit;
using System.Web.SessionState;

namespace Harbour.RedisSessionStateStore.Tests
{
    public class RedisSessionStateTests
    {
        // 2011-12-22 at 1:1:1 UTC
        private byte[] date1Bytes = new byte[] { 128, 68, 113, 78, 92, 142, 206, 8 };

        // 2011-11-22 at 1:1:1 UTC
        private byte[] date2Bytes = new byte[] { 128, 196, 12, 86, 201, 118, 206, 8 };
        
        // { name: "Felix", age: 1 }
        private byte[] itemsBytes = new byte[] { 2, 0, 0, 0, 255, 255, 255, 255, 4, 110, 97, 109, 101, 3, 97, 103, 101, 7, 0, 0, 0, 12, 0, 0, 0, 1, 5, 70, 101, 108, 105, 120, 2, 1, 0, 0, 0 };
        
        // 999
        private byte[] lockIdBytes = new byte[] { 231, 3, 0, 0 };

        [Fact]
        public void ToMap()
        {
            var data = new RedisSessionState()
            {
                Created = new DateTime(2011, 12, 22, 1, 1, 1, DateTimeKind.Utc),
                Locked = true,
                LockId = 999,
                LockDate = new DateTime(2011, 11, 22, 1, 1, 1, DateTimeKind.Utc),
                Timeout = 3,
                Flags = SessionStateActions.InitializeItem
            };

            data.Items["name"] = "Felix";
            data.Items["age"] = 1;

            var map = data.ToMap();
            Assert.Equal(date1Bytes, map["created"]);
            Assert.Equal(new byte[] { 1 }, map["locked"]);
            Assert.Equal(lockIdBytes, map["lockId"]);
            Assert.Equal(date2Bytes, map["lockDate"]);
            Assert.Equal(new byte[] { 3, 0, 0, 0 }, map["timeout"]);
            Assert.Equal(new byte[] { 1, 0, 0, 0 }, map["flags"]);
            Assert.Equal(itemsBytes, map["items"]);
        }

        [Fact]
        public void TryParse_should_fail_if_null_data()
        {
            RedisSessionState data;
            Assert.False(RedisSessionState.TryParse(null, out data));
        }

        [Fact]
        public void TryParse_should_fail_if_incorrect_length()
        {
            RedisSessionState data;
            var raw = new Dictionary<string, byte[]>();
            Assert.False(RedisSessionState.TryParse(raw, out data));
        }

        [Fact]
        public void TryParse_should_pass_with_valid_data()
        {
            var raw = new Dictionary<string, byte[]>()
            {
                { "created", date1Bytes },
                { "locked", new byte[] { 1 } },
                { "lockId", lockIdBytes },
                { "lockDate", date2Bytes },
                { "timeout", new byte[] { 3, 0, 0, 0 } },
                { "flags", new byte[] { 1, 0, 0, 0 } },
                { "items", itemsBytes }
            };

            RedisSessionState data;
            Assert.True(RedisSessionState.TryParse(raw, out data));
            Assert.Equal(new DateTime(2011, 12, 22, 1, 1, 1, DateTimeKind.Utc), data.Created);
            Assert.True(data.Locked);
            Assert.Equal(999, data.LockId);
            Assert.Equal(new DateTime(2011, 11, 22, 1, 1, 1, DateTimeKind.Utc), data.LockDate);
            Assert.Equal(3, data.Timeout);
            Assert.Equal(SessionStateActions.InitializeItem, data.Flags);
            Assert.Equal(2, data.Items.Count);
            Assert.Equal("Felix", data.Items["name"]);
            Assert.Equal(1, data.Items["age"]);
        }
    }
}
