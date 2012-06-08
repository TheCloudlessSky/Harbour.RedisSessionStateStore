using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ServiceStack.Redis;
using ServiceStack.Text;

namespace Harbour.RedisSessionStateStore
{
    internal static class RedisClientExtensions
    {
        private const int Success = 1;

        public static void SetRangeInHashRaw(this IRedisClient client, string hashId, IEnumerable<KeyValuePair<string, byte[]>> keyValuePairs)
        {
            var keyValuePairsList = keyValuePairs.ToList();
            if (keyValuePairsList.Count == 0) return;

            var keys = new byte[keyValuePairsList.Count][];
            var values = new byte[keyValuePairsList.Count][];

            for (var i = 0; i < keyValuePairsList.Count; i++)
            {
                var kvp = keyValuePairsList[i];
                keys[i] = kvp.Key.ToUtf8Bytes();
                values[i] = kvp.Value;
            }

            ((IRedisNativeClient)client).HMSet(hashId, keys, values);
        }

        public static Dictionary<string, byte[]> GetAllEntriesFromHashRaw(this IRedisClient client, string hashId)
        {
            var multiData = ((IRedisNativeClient)client).HGetAll(hashId);
            return MultiByteArrayToDictionary(multiData);
        }

        internal static Dictionary<string, byte[]> MultiByteArrayToDictionary(byte[][] multiData)
        {
            var map = new Dictionary<string, byte[]>();

            for (var i = 0; i < multiData.Length; i += 2)
            {
                var key = multiData[i].FromUtf8Bytes();
                map[key] = multiData[i + 1];
            }

            return map;
        }

        public static byte[] GetValueFromHashRaw(this IRedisClient client, string hashId, string key)
        {
            return ((IRedisNativeClient)client).HGet(hashId, key.ToUtf8Bytes());
        }

        public static bool SetEntryInHashIfNotExists(this IRedisClient client, string hashId, string key, byte[] value)
        {
            return ((IRedisNativeClient)client).HSetNX(hashId, key.ToUtf8Bytes(), value) == Success;
        }
    }
}
