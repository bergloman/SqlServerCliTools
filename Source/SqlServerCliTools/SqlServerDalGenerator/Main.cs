using System;
using System.IO;
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

            if (args.Length != 1)
                throw new ArgumentOutOfRangeException("args", args.Length, "Invalid number of command-line parameters. Expecting 1.");
            if (!File.Exists(args[0]))
                throw new ArgumentOutOfRangeException("Config file", args[0], "Specified file doesn't exist.");

            Trace.Listeners.Add(new TextWriterTraceListener(System.Console.Out));

            Trace.WriteLine("Reading settings file...");
            string s;
            using (StreamReader sr = new StreamReader(args[0])) {
                s = sr.ReadToEnd();
            }
            var pars = (CodeGenerationParameters)Utils.DeserializeXml(s, typeof(CodeGenerationParameters));
            pars.export_file_name = pars.local_file;

            // perform code generation

            try {
                Trace.WriteLine("Generating code...");
                CodeGenerator cg = new CodeGenerator(pars);
                cg.CreateCsCode();

                Trace.WriteLine("Done.");
            } catch (Exception ex) {
                Trace.WriteLine(ex.ToString());
                throw ex;
            }
        }
    }
}
