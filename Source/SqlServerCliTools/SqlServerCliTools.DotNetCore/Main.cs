using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections;
using System.Configuration;
using System.Diagnostics;

namespace DalGenerator {

    class MainClass {

        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main(string[] args) {

            if (args.Length != 1) {
                //throw new ArgumentOutOfRangeException("args", args.Length, "Invalid number of command-line parameters. Expecting 1.");
            }
            var file_name = "../../../examples/example1.xml";// args[0];
            if (!File.Exists(file_name)) {
                throw new ArgumentOutOfRangeException("Config file", file_name, "Specified file doesn't exist.");
            }

            Trace.Listeners.Add(new TextWriterTraceListener(System.Console.Out));

            Trace.WriteLine("Reading settings file...");
            string s;
            using (var sr = new StreamReader(file_name)) {
                s = sr.ReadToEnd();
            }
            var pars = (CodeGenerationParameters)Utils.DeserializeXml(s, typeof(CodeGenerationParameters));
            pars.selected_tables = pars.selected_tables
                .Where(x => x.Trim().Length > 0)
                .ToArray();

            // perform code generation
            try {
                Trace.WriteLine("Generating code...");
                var cg = new CodeGenerator(pars);
                cg.CreateCsCode();

                Trace.WriteLine("Done.");
            } catch (Exception ex) {
                Trace.WriteLine(ex.ToString());
                throw ex;
            }
        }
    }
}
