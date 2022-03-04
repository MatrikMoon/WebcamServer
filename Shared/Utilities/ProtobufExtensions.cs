using ProtoBuf;
using Shared.Models;

/**
 * Created by Moon on 9/9/2021
 * Extension methods for working with these proto packets
 * Particularly, this helper came around when the need arose for custom equality between proto packets
 */

namespace Shared.Utilities
{
    public static class ProtobufExtensions
    {
        public static bool UserEquals(this User firstUser, User secondUser)
        {
            return firstUser.Id == secondUser.Id;
        }

        public static bool ContainsUser(this ICollection<User> users, User user)
        {
            return users.Any(x => x.UserEquals(user));
        }

        public static byte[] ProtoSerialize<T>(this T record) where T : class
        {
            using var stream = new MemoryStream();
            Serializer.Serialize(stream, record);
            return stream.ToArray();
        }

        public static T ProtoDeserialize<T>(this byte[] data) where T : class
        {
            using var stream = new MemoryStream(data);
            return Serializer.Deserialize<T>(stream);
        }
    }
}
