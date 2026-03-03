

namespace CosmoS3.Classes
{
    public class User
    {
        public int Id { get; set; } = 0;
        public string GUID { get; set; } = GuidSortable.NewGuid().ToString();
        public string Name { get; set; } = null;
        public string Email { get; set; } = null;
        public DateTime CreatedUtc { get; set; } = DateTime.Now.ToUniversalTime();
        public User()
        {

        }

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="name">Name.</param>
        /// <param name="email">Email.</param>
        public User(string name, string email)
        {
            if (String.IsNullOrEmpty(name)) throw new ArgumentNullException(nameof(name));
            if (String.IsNullOrEmpty(email)) throw new ArgumentNullException(nameof(email));

            GUID = Guid.NewGuid().ToString();
            Name = name;
            Email = email;
        }

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="guid">GUID.</param>
        /// <param name="name">Name.</param>
        /// <param name="email">Email.</param>
        public User(string guid, string name, string email)
        {
            if (String.IsNullOrEmpty(guid)) throw new ArgumentNullException(nameof(guid));
            if (String.IsNullOrEmpty(name)) throw new ArgumentNullException(nameof(name));
            if (String.IsNullOrEmpty(email)) throw new ArgumentNullException(nameof(email));

            GUID = guid;
            Name = name;
            Email = email;
        }
    }
}
