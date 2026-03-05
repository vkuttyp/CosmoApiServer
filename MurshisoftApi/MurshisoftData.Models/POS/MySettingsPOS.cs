namespace MurshisoftData.Models.POS
{
    public enum UserRight { None, Open, Add, Edit, Delete };
    public enum UpdateMode { None = 0, New = 1, Edit = 2 };
    public enum PosType { Restaurant,NormalPOS,PharmacyPOS}
    public static class MySettingsPOS
    {
        static string CashDrawerCode;
        public static byte[] GetCashdrawerOpenCode()
        {
            CashDrawerCode=SessionInfoPOS.SessionData.PosSettings.CashDrawerCode;
            string[] numbers = CashDrawerCode.Split(',');
            byte[] bytes = new byte[numbers.Length];
            for (int i = 0; i < numbers.Length; i++)
            {
                bytes[i] = byte.Parse(numbers[i]);
            }
            return bytes;
        }
        //public static bool EnableOffline=false;
        


        public static PosType PosType= PosType.NormalPOS;
        public static string Language = "Arabic";
        public static bool IsHijri;
    }
}
