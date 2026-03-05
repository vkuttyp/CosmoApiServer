using MurshisoftData.Models.POS;
using System;
using System.IO;
using System.Text.Json;
using System.Xml.Linq;
using System.Xml.Serialization;

namespace MurshisoftData.Models
{
    public static class ObjectCopier
    {
        public static T Clone<T>(T source)
        {
            var serialized = JsonSerializer.Serialize(source);
            return JsonSerializer.Deserialize<T>(serialized);
        }
    }

   public class Utilities
    {
        public static T ReadFromJsonFile<T>(string filePath) where T : new()
        {
            TextReader reader = null;
            if (!File.Exists(filePath)) return default(T);
            try
            {
                reader = new StreamReader(filePath);
                var fileContents = reader.ReadToEnd();
                return JsonSerializer.Deserialize<T>(fileContents);
            }
            finally
            {
                if (reader != null)
                    reader.Close();
            }
        }
        public static decimal Round(decimal number)
        {
            if (number == 0) return 0;
            int decimalPoints = 2;
            decimal decimalPowerOfTen = (decimal)Math.Pow(10, decimalPoints);
            var nu = number * decimalPowerOfTen + 0.5M;
            var val= Math.Floor(nu) / decimalPowerOfTen;
            return val;
        }
        public static DateTime GetDateFromString(String strDate)
        {
            DateTime retvalue = DateTime.Today;
            try
            {
                string sDate = strDate.Replace("/", "");
                sDate = sDate.Replace(" ", "");
                int year, month, day;
                year = int.Parse(sDate.Substring(4, 4));
                month = int.Parse(sDate.Substring(2, 2));
                day = int.Parse(sDate.Substring(0, 2));
                System.Globalization.Calendar cal;
                if (MySettingsPOS.IsHijri)
                    cal = new System.Globalization.UmAlQuraCalendar();
                else cal = new System.Globalization.GregorianCalendar();
                //if (SessionInfo.IsHijri)
                //    cal = new System.Globalization.UmAlQuraCalendar();
                //else
                //    cal = new System.Globalization.GregorianCalendar();
                retvalue = new DateTime(year, month, day, cal);
            }
            catch (Exception)
            {
                throw;
            }
            return retvalue;
        }


        public static bool IsNumeric(string str)
        {
            if (str == null || str.Length == 0)
                return false;
            foreach (char c in str)
            {
                if (!Char.IsNumber(c))
                {
                    return false;
                }
            }
            return true;
        }

        //Returns string of exact change
        public static string CalculateChange(decimal payment, decimal cost)
        {

            //array of every value of tender that can be returned
            decimal[] tenderValues = new decimal[] {500, 200, 100, 50, 20, 10, 5, 1, 0.25M, 0.1M, 0.05M, 0.01M };

            //get change which is going to be returned
            decimal change = 0;
            change = payment - cost;

            //check that they payed enough
            if (change < 0)
            {
                return "";
            }

            //this is our output that will display what we are going to return
            string output = "";// "الصرف:" + Environment.NewLine;

            //go through each tender value and calc return
            foreach (decimal t in tenderValues)
            {

                //the whole number of tender value / change will give you how many of that value you can return
                int num = (int)Math.Truncate(change / t);

                //if it can return atleast 1 report it then remove it from the change
                if ((num > 0))
                {
                    output += t.ToString().PadLeft(3,' ') + " : " + num.ToString() + Environment.NewLine;
                    change -= num * t;

                }
            }

            //return our output and display it where needed such as a textbox
            return output;
        }

        public static string CalculateChange(decimal change)
        {

            //array of every value of tender that can be returned
            decimal[] tenderValues = new decimal[] { 500, 200, 100, 50, 20, 10, 5, 1, 0.25M, 0.1M, 0.05M, 0.01M };

            //get change which is going to be returned
            //decimal change = 0;
            //change = payment - cost;

            //check that they payed enough
            if (change < 0)
            {
                return "";
            }

            //this is our output that will display what we are going to return
            string output = "";// "الصرف:" + Environment.NewLine;

            //go through each tender value and calc return
            foreach (decimal t in tenderValues)
            {

                //the whole number of tender value / change will give you how many of that value you can return
                int num = (int)Math.Truncate(change / t);

                //if it can return atleast 1 report it then remove it from the change
                if ((num > 0))
                {
                    output += t.ToString().PadLeft(3, ' ') + " : " + num.ToString() + Environment.NewLine;
                    change -= num * t;

                }
            }

            //return our output and display it where needed such as a textbox
            return output;
        }
        public static T Deserialize<T>(string input, string nameSpace) where T : class
        {
            var ser = new XmlSerializer(typeof(T), new XmlRootAttribute(nameSpace));

            using (StringReader sr = new StringReader(input))
            {
                return (T)ser.Deserialize(sr);
            }
        }
        public static T DeSerializeElement<T>(XElement element, string nameSpace)
        {
            var serializer = new XmlSerializer(typeof(T),new XmlRootAttribute(nameSpace));
            return (T)serializer.Deserialize(element.CreateReader());
        }
    }
}
