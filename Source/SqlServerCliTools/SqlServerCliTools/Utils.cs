using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SqlServerCliTools {

    public class Utils {

        public static string CreateConnectionString(string server, string db, string u, string p) {
            return string.Format("Data Source={0};Initial Catalog={1};UId={2};Pwd={3};", server, db, u, p);
        }
        public static string CreateConnectionString(string server, string db) {
            return string.Format("Data Source={0};Initial Catalog={1};Integrated Security=SSPI;", server, db);
        }

        public static string PurgeText(string s) {
            string xs = s.ToLower();

            xs = xs.Replace("set quoted_identifier on", "");
            xs = xs.Replace("set quoted_identifier off", "");
            xs = xs.Replace("set ansi_nulls on", "");
            xs = xs.Replace("set ansi_nulls off", "");
            xs = xs.Replace("[", "");
            xs = xs.Replace("]", "");
            xs = xs.Replace("( ", "(");
            xs = xs.Replace(" )", ")");
            xs = xs.Replace("collate slovenian_ci_as", ""); // TODO
            xs = xs.Replace("top (100) percent", "top 100 percent");
            xs = xs.Replace("((0))", "(0)");
            xs = xs.Replace("(0)", "0");
            xs = xs.Replace("((1))", "(1)");
            xs = xs.Replace("(1)", "1");

            xs = xs.Replace("((99))", "(99)");
            xs = xs.Replace("((999))", "(999)");
            xs = xs.Replace("textimage_on primary", "");
            xs = xs.Replace("on primary", "");

            string nl = Environment.NewLine;
            xs = xs.Replace("\t", " ");
            xs = xs.Replace("\r", "\n");

            bool do_loop = true;
            while (do_loop) {
                xs = xs.Replace(" \n", "\n");
                xs = xs.Replace("\n\n", "\n");

                xs = xs.Replace(" " + nl, nl);
                xs = xs.Replace(nl + nl, nl);
                xs = xs.Replace("  ", " ");

                do_loop =
                    (xs.IndexOf(" \n") >= 0) ||
                    (xs.IndexOf("\n\n") >= 0) ||
                    (xs.IndexOf(" " + nl) >= 0) ||
                    (xs.IndexOf(nl + nl) >= 0) ||
                    (xs.IndexOf("  ") >= 0);
            }
            if (xs.EndsWith("\n"))
                xs = xs.Remove(xs.Length - 1, 1);

            return xs;
        }
    }
}
