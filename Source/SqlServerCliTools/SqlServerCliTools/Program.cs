using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SqlServerCliTools {

    class Program {

        static void Main(string[] args) {
            if (args.Length!=3) {
                Console.WriteLine("Invalid arguments for this program. Usage:");
                Console.WriteLine("SqlServerCliTools <server> <database> <output_dir>");
            }

            var server = args[0];
            var db_name = args[1];
            var output_dir = args[2];

            var conn_str = Utils.CreateConnectionString(server, db_name);
            var db = new Database(conn_str);
            db.LoadObjects(false);

            foreach (var tab in db.UserTables) {
                var tab_obj = tab.Value;
                var sb = new StringBuilder();
                foreach (var item in tab_obj.Columns.OrderBy(x => x.Value.Name)) {
                    var col = item.Value;
                    sb.AppendLine($"{col.Name} {col.Type} len={col.Length} scale={col.Scale} nullable={col.Nullable}");
                }
                File.WriteAllText(Path.Combine(output_dir, $"tab_{tab_obj.Name}.txt"), sb.ToString());
            }
            foreach (var view in db.Views) {
                File.WriteAllText(Path.Combine(output_dir, $"view_{view.Value.Name}.sql"), view.Value.TextDefinitionClean);
            }
            foreach (var sp in db.StoredProcs) {
                File.WriteAllText(Path.Combine(output_dir, $"sp_{sp.Value.Name}.sql"), sp.Value.TextDefinitionClean);
            }
            foreach (var f in db.Functions) {
                File.WriteAllText(Path.Combine(output_dir, $"func_{f.Value.Name}.sql"), f.Value.TextDefinitionClean);
            }
            foreach (var ind in db.Indexes) {
                File.WriteAllText(Path.Combine(output_dir, $"index_{ind.Value.Name}.sql"), ind.Value.TextDefinitionClean);
            }
            foreach (var cc in db.CheckConstraints) {
                File.WriteAllText(Path.Combine(output_dir, $"constr_{cc.Value.Name}.sql"), cc.Value.TextDefinitionClean);
            }
            foreach (var d in db.Defaults) {
                File.WriteAllText(Path.Combine(output_dir, $"default_{d.Value.Name}.sql"), d.Value.TextDefinitionClean);
            }



            //var conn_str2 = Utils.CreateConnectionString("localhost", "carvic_cal_xx");
            //var db2 = new Database(conn_str2);
            //db2.LoadObjects(false);
            //var diffs = db.CompareTo(db2);

            //var i = 0;
            //foreach (var item in diffs) {
            //    if (i++ == 0) {
            //        var name = item.Name;
            //        var tab = db.UserTables[name];
            //    }
            //    Console.WriteLine("{0}, {1}, {2}", item.Name, item.Status, item.Type);
            //}

        }
    }
}
