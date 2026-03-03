

namespace CosmoS3.Classes
{
    public class ObjectAcl
    {
        public int Id { get; set; } = 0;
        public string GUID { get; set; } = Guid.NewGuid().ToString();
        public string UserGroup { get; set; } = null;
        public string UserGUID { get; set; } = null;
        public string IssuedByUserGUID { get; set; } = null;
        public string BucketGUID { get; set; } = null;
        public string ObjectGUID { get; set; } = null;
        public bool PermitRead { get; set; } = false;
        public bool PermitWrite { get; set; } = false;
        public bool PermitReadAcp { get; set; } = false;
        public bool PermitWriteAcp { get; set; } = false;

        public bool FullControl { get; set; } = false;
        public DateTime CreatedUtc { get; set; } = DateTime.Now.ToUniversalTime();
        public ObjectAcl()
        {

        }
         
        /// <summary>
        /// Create a group ACL.
        /// </summary>
        /// <param name="groupName">Group name.</param>
        /// <param name="issuedByUserGuid">Issued by user GUID.</param>
        /// <param name="bucketGuid">Bucket GUID.</param>
        /// <param name="objectGuid">Object GUID.</param>
        /// <param name="permitRead">Permit read.</param>
        /// <param name="permitWrite">Permit write.</param>
        /// <param name="permitReadAcp">Permit access control read.</param>
        /// <param name="permitWriteAcp">Permit access control write.</param>
        /// <param name="fullControl">Full control.</param>
        /// <returns>Instance.</returns>
        public static ObjectAcl GroupAcl(
            string groupName, 
            string issuedByUserGuid, 
            string bucketGuid,
            string objectGuid,  
            bool permitRead,
            bool permitWrite,
            bool permitReadAcp,
            bool permitWriteAcp,
            bool fullControl)
        {
            if (String.IsNullOrEmpty(groupName)) throw new ArgumentNullException(nameof(groupName));
            if (String.IsNullOrEmpty(issuedByUserGuid)) throw new ArgumentNullException(nameof(issuedByUserGuid));
            if (String.IsNullOrEmpty(bucketGuid)) throw new ArgumentNullException(nameof(bucketGuid));
            if (String.IsNullOrEmpty(objectGuid)) throw new ArgumentNullException(nameof(objectGuid)); 

            ObjectAcl ret = new ObjectAcl();

            ret.UserGroup = groupName;
            ret.UserGUID = null;
            ret.IssuedByUserGUID = issuedByUserGuid;
            ret.BucketGUID = bucketGuid;
            ret.ObjectGUID = objectGuid;

            ret.PermitRead = permitRead;
            ret.PermitWrite = permitWrite;
            ret.PermitReadAcp = permitReadAcp;
            ret.PermitWriteAcp = permitWriteAcp;
            ret.FullControl = fullControl;

            return ret;
        }

        /// <summary>
        /// Create a user ACL.
        /// </summary>
        /// <param name="userGuid">User GUID.</param>
        /// <param name="issuedByUserGuid">Issued by user GUID.</param>
        /// <param name="bucketGuid">Bucket GUID.</param>
        /// <param name="objectGuid">Object GUID.</param>
        /// <param name="permitRead">Permit read.</param>
        /// <param name="permitWrite">Permit write.</param>
        /// <param name="permitReadAcp">Permit access control read.</param>
        /// <param name="permitWriteAcp">Permit access control write.</param>
        /// <param name="fullControl">Full control.</param>
        /// <returns>Instance.</returns>
        public static ObjectAcl UserAcl(
            string userGuid, 
            string issuedByUserGuid, 
            string bucketGuid,
            string objectGuid,  
            bool permitRead,
            bool permitWrite,
            bool permitReadAcp,
            bool permitWriteAcp,
            bool fullControl)
        {
            if (String.IsNullOrEmpty(userGuid)) throw new ArgumentNullException(nameof(userGuid));
            if (String.IsNullOrEmpty(issuedByUserGuid)) throw new ArgumentNullException(nameof(issuedByUserGuid));
            if (String.IsNullOrEmpty(objectGuid)) throw new ArgumentNullException(nameof(objectGuid)); 

            ObjectAcl ret = new ObjectAcl();

            ret.UserGroup = null;
            ret.UserGUID = userGuid;
            ret.IssuedByUserGUID = issuedByUserGuid;
            ret.BucketGUID = bucketGuid;
            ret.ObjectGUID = objectGuid;

            ret.PermitRead = permitRead;
            ret.PermitWrite = permitWrite;
            ret.PermitReadAcp = permitReadAcp;
            ret.PermitWriteAcp = permitWriteAcp;
            ret.FullControl = fullControl;

            return ret;
        }
           


        /// <summary>
        /// Create a human-readable string of the object.
        /// </summary>
        /// <returns>String.</returns>
        public override string ToString()
        {
            string
                ret = "--- Object ACL " + Id + " ---" + Environment.NewLine +
                "  User group      : " + UserGroup + Environment.NewLine +
                "  User GUID       : " + UserGUID + Environment.NewLine +
                "  Issued by       : " + IssuedByUserGUID + Environment.NewLine +
                "  Bucket GUID     : " + BucketGUID + Environment.NewLine +
                "  Object GUID     : " + ObjectGUID + Environment.NewLine +
                "  Permissions     : " + Environment.NewLine +
                "    READ          : " + PermitRead.ToString() + Environment.NewLine +
                "    WRITE         : " + PermitWrite.ToString() + Environment.NewLine +
                "    READ_ACP      : " + PermitReadAcp.ToString() + Environment.NewLine +
                "    WRITE_ACP     : " + PermitWriteAcp.ToString() + Environment.NewLine +
                "    FULL_CONTROL  : " + FullControl.ToString() + Environment.NewLine; 

            return ret;
        }
    }
}
