using System;
using System.Data.SqlClient;
using System.Data;
using System.Collections.Generic;

namespace SqlServerSchemaExport {

    public abstract class DatabaseObject : IComparable {

        public DatabaseObject(string name, int id) {
            this.Name = name.ToLower();
            this.Id = id;
            this.Columns = new Dictionary<string, Column>();
        }

        public string Name { get; set; }
        public int Id { get; set; }
        public Dictionary<string, Column> Columns { get; set; }

        public virtual void GatherData(SqlConnection conn) {
            GetColumnData(conn);
        }

        private void GetColumnData(SqlConnection conn) {
            using (SqlCommand command = conn.CreateCommand()) {
                command.CommandText = "select name, xtype, length, xscale, isnullable from syscolumns where id=@id order by colorder";
                command.Parameters.Add("@id", System.Data.SqlDbType.Int).Value = this.Id;

                SqlDataAdapter ad = new SqlDataAdapter(command);
                DataTable tab = new DataTable();
                ad.Fill(tab);

                foreach (DataRow r in tab.Rows) {
                    string name = r["name"].ToString().ToUpper();
                    int t = (byte)r["xtype"];
                    int l = (short)r["length"];
                    int c = (byte)r["xscale"];
                    int nullable = (int)r["isnullable"];
                    Columns[name] = new Column(name, t, l, c, nullable == 1);
                }
            }
        }

        public bool CompareTo(DatabaseObject obj) {
            return CompareColumns(obj) && LocalCompare(obj);
        }

        protected virtual bool LocalCompare(DatabaseObject obj) {
            return true;
        }

        private bool CompareColumns(DatabaseObject obj) {
            if (this.Columns.Values.Count != obj.Columns.Values.Count)
                return false;
            foreach (Column c in this.Columns.Values) {
                string name = c.Name.ToUpper();
                if (!obj.Columns.ContainsKey(name))
                    return false;
                if (!c.CompareTo(obj.Columns[name]))
                    return false;
            }
            return true;
        }

        int IComparable.CompareTo(object obj) {
            if (obj is DatabaseObject)
                return Name.CompareTo((obj as DatabaseObject).Name);
            return 0;
        }
    }

    public class Column {

        public Column(string name, int type, int length, int scale, bool nullable) {
            this.Name = name.ToLower();
            this.Type = type;
            this.Length = length;
            this.Scale = scale;
            this.Nullable = nullable;
        }

        public string Name { get; set; }
        public int Type { get; set; }
        public int Length { get; set; }
        public int Scale { get; set; }
        public bool Nullable { get; set; }

        public bool CompareTo(Column c) {
            return
               this.Name == c.Name &&
               this.Type == c.Type &&
                this.Length == c.Length &&
                this.Scale == c.Scale &&
                this.Nullable == c.Nullable;
        }
    }

    public class UserTable : DatabaseObject {
        public UserTable(string name, int id)
            : base(name, id) {
        }
    }

    public class View : DatabaseObject {
        private string textDefinition;
        string textDefinition_clean;
        bool compareAnsiNullsAndQuotedIdent = false;

        public View(string name, int id, bool use_ansi_nulls_and_quoted_ident)
            : base(name, id) {
            compareAnsiNullsAndQuotedIdent = use_ansi_nulls_and_quoted_ident;
        }

        public override void GatherData(SqlConnection conn) {
            base.GatherData(conn);
            using (SqlCommand command = conn.CreateCommand()) {
                if (compareAnsiNullsAndQuotedIdent) {
                    command.CommandText = @"
                                    select 
                                        text,
                                        OBJECTPROPERTY(s.id,'ExecIsQuotedIdentOn') AS IsQuotedIdentOn,
                                        OBJECTPROPERTY(s.id,'ExecIsAnsiNullsOn') AS IsAnsiNullsOn
                                    from 
                                        sysobjects s
                                        inner join syscomments c on s.id = c.id
                                    where c.id = @id";
                } else {
                    command.CommandText = @"select text from syscomments where id=@id";
                }
                command.Parameters.Add("@id", SqlDbType.Int).Value = this.Id;
                using (SqlDataReader reader = command.ExecuteReader()) {
                    while (reader.Read()) {
                        textDefinition += reader.GetString(0);
                        if (compareAnsiNullsAndQuotedIdent) {
                            IsQuotedIdentifier = Convert.ToBoolean(reader.GetInt32(1));
                            IsAnsiNulls = Convert.ToBoolean(reader.GetInt32(2));
                        } else {
                            IsQuotedIdentifier = false;
                            IsAnsiNulls = true;
                        }
                    }
                    textDefinition = textDefinition.Trim();
                }
            }

            textDefinition_clean = Utils.PurgeText(textDefinition);
        }

        public bool IsQuotedIdentifier { get; set; }
        public bool IsAnsiNulls { get; set; }

        public string TextDefinitionClean {
            get { return textDefinition_clean; }
        }

        public string TextDefinition {
            get { return textDefinition; }
            set { textDefinition = value; }
        }

        protected override bool LocalCompare(DatabaseObject obj) {
            if (obj is View) {
                if (compareAnsiNullsAndQuotedIdent) {
                    return
                        this.textDefinition_clean == ((View)obj).textDefinition_clean &&
                        ((View)obj).IsQuotedIdentifier == this.IsQuotedIdentifier &&
                        ((View)obj).IsAnsiNulls == this.IsAnsiNulls;
                } else {
                    return this.textDefinition_clean == ((View)obj).textDefinition_clean;
                }
            }
            return false;
        }
    }

    public enum ConstraintType {
        DefaultConstraint,
        CheckConstraint
    }

    /// <summary>
    /// Summary description for Constraint.
    /// </summary>
    public class Constraint : DatabaseObject {
        string textDefinition = "";
        string textDefinition_clean = "";
        ConstraintType constraint_type;

        public Constraint(string name, int id, ConstraintType constraint_type) : base(name, id) {
            this.constraint_type = constraint_type;
        }

        public override void GatherData(SqlConnection conn) {
            base.GatherData(conn);
            using (SqlCommand command = conn.CreateCommand()) {
                command.CommandText = @"select a.[text] from syscomments a where a.id = @id";
                command.Parameters.AddWithValue("@id", this.Id);
                using (SqlDataReader reader = command.ExecuteReader()) {
                    while (reader.Read())
                        textDefinition += reader.GetString(0);
                    textDefinition = textDefinition.Trim().ToLower();
                }
            }

            textDefinition = textDefinition.Replace("(", " ( ").Replace(")", " ) ").Replace("=", " = ").Trim();
            while (textDefinition.IndexOf("  ") > 0)
                textDefinition = textDefinition.Replace("  ", " ").Trim();

            while (textDefinition.StartsWith("(") && textDefinition.EndsWith(")")) {
                if (textDefinition.IndexOf(")") < textDefinition.IndexOf("("))
                    break;
                textDefinition = textDefinition.Remove(textDefinition.Length - 1, 1).Remove(0, 1).Trim();
            }

            textDefinition_clean = Utils.PurgeText(textDefinition);
        }

        public string TextDefinitionClean {
            get { return textDefinition_clean; }
        }

        public string TextDefinition {
            get { return textDefinition; }
            set { textDefinition = value; }
        }

        protected override bool LocalCompare(DatabaseObject obj) {
            if (obj is Constraint)
                return this.textDefinition_clean == ((Constraint)obj).textDefinition_clean;
            return false;
        }

    }
    public class Index : DatabaseObject {
        string textDefinition = "";
        string textDefinition_clean = "";
        int indid = -1;

        public Index(string name, int id, int indid)
            : base(name, id) {
            this.indid = indid;
        }

        public override void GatherData(SqlConnection conn) {
            using (SqlCommand command = conn.CreateCommand()) {
                command.CommandText = @"
                    select 
                        object_name(i.id) + '.' +
                        c.name + ' ' +
                        case indexkey_property(i.id, i.indid, ik.keyno, 'IsDescending')
                            when 1 then 'DESC'
                            else 'ASC'
                        end 
                    from
                        sysindexes i
                        inner join sysindexkeys ik on ik.id = i.id and ik.indid = i.indid
                        inner join syscolumns c on c.id = ik.id and c.colid =  ik.colid
                    where
                        i.id = @id and
                        i.indid = @indid
                    order by ik.keyno";

                command.Parameters.AddWithValue("@id", this.Id);
                command.Parameters.AddWithValue("@indid", this.indid);
                using (SqlDataReader reader = command.ExecuteReader()) {
                    while (reader.Read())
                        textDefinition += reader.GetString(0).Trim() + Environment.NewLine;

                    textDefinition = textDefinition.Trim();
                }
            }

            textDefinition_clean = Utils.PurgeText(textDefinition);
        }

        public string TextDefinitionClean {
            get { return textDefinition_clean; }
        }

        public string TextDefinition {
            get { return textDefinition; }
            set { textDefinition = value; }
        }

        protected override bool LocalCompare(DatabaseObject obj) {
            bool res = false;
            if (obj is Index) {
                Index index_obj = (Index)obj;
                res = this.textDefinition_clean == index_obj.textDefinition_clean;
            }
            return res;
        }
    }

    public class Function : DatabaseObject {
        string textDefinition;
        string textDefinition_clean;
        bool compareAnsiNullsAndQuotedIdent = false;

        public Function(string name, int id, bool use_ansi_nulls_and_quoted_ident)
            : base(name, id) {
            compareAnsiNullsAndQuotedIdent = use_ansi_nulls_and_quoted_ident;
        }

        public override void GatherData(SqlConnection conn) {
            base.GatherData(conn);
            using (SqlCommand command = conn.CreateCommand()) {
                if (compareAnsiNullsAndQuotedIdent) {
                    command.CommandText = @"
                                  select 
                                        text,
                                        OBJECTPROPERTY(s.id,'ExecIsQuotedIdentOn') AS IsQuotedIdentOn,
                                        OBJECTPROPERTY(s.id,'ExecIsAnsiNullsOn') AS IsAnsiNullsOn
                                    from 
                                        sysobjects s
                                        inner join syscomments c on s.id = c.id
                                    where c.id = @id";
                } else {
                    command.CommandText = @"select text from syscomments where id=@id";
                }
                command.Parameters.Add("@id", SqlDbType.Int).Value = this.Id;
                using (SqlDataReader reader = command.ExecuteReader()) {
                    while (reader.Read()) {
                        textDefinition += reader.GetString(0);
                        if (compareAnsiNullsAndQuotedIdent) {
                            IsQuotedIdentifier = Convert.ToBoolean(reader.GetInt32(1));
                            IsAnsiNulls = Convert.ToBoolean(reader.GetInt32(2));
                        } else {
                            IsQuotedIdentifier = false;
                            IsAnsiNulls = true;
                        }
                    }
                    textDefinition = textDefinition.Trim();
                }
            }

            textDefinition_clean = Utils.PurgeText(textDefinition);
        }

        public bool IsQuotedIdentifier { get; set; }
        public bool IsAnsiNulls { get; set; }

        public string TextDefinitionClean {
            get { return textDefinition_clean; }
        }

        public string TextDefinition {
            get { return textDefinition; }
            set { textDefinition = value; }
        }

        protected override bool LocalCompare(DatabaseObject obj) {
            if (obj is Function) {
                if (compareAnsiNullsAndQuotedIdent) {
                    return
                        this.textDefinition_clean == ((Function)obj).textDefinition_clean &&
                        ((Function)obj).IsQuotedIdentifier == this.IsQuotedIdentifier &&
                        ((Function)obj).IsAnsiNulls == this.IsAnsiNulls;
                } else {
                    return this.textDefinition_clean == ((Function)obj).textDefinition_clean;
                }
            }
            return false;
        }

    }

    public class StoredProc : DatabaseObject {
        string textDefinition;
        string textDefinition_clean;
        bool compareAnsiNullsAndQuotedIdent = false;

        public StoredProc(string name, int id, bool use_ansi_nulls_and_quoted_ident)
            : base(name, id) {
            compareAnsiNullsAndQuotedIdent = use_ansi_nulls_and_quoted_ident;
        }

        public override void GatherData(SqlConnection conn) {
            base.GatherData(conn);
            using (SqlCommand command = conn.CreateCommand()) {
                if (compareAnsiNullsAndQuotedIdent) {
                    command.CommandText = @"
                                    select 
                                        text,
                                        OBJECTPROPERTY(s.id,'ExecIsQuotedIdentOn') AS IsQuotedIdentOn,
                                        OBJECTPROPERTY(s.id,'ExecIsAnsiNullsOn') AS IsAnsiNullsOn
                                    from 
                                        sysobjects s
                                        inner join syscomments c on s.id = c.id
                                    where c.id = @id";
                } else {
                    command.CommandText = @"select text from syscomments where id=@id";
                }
                command.Parameters.Add("@id", SqlDbType.Int).Value = this.Id;
                using (SqlDataReader reader = command.ExecuteReader()) {
                    while (reader.Read()) {
                        textDefinition += reader.GetString(0);
                        if (compareAnsiNullsAndQuotedIdent) {
                            IsQuotedIdentifier = Convert.ToBoolean(reader.GetInt32(1));
                            IsAnsiNulls = Convert.ToBoolean(reader.GetInt32(2));
                        } else {
                            IsQuotedIdentifier = false;
                            IsAnsiNulls = true;
                        }
                    }
                    textDefinition = textDefinition.Trim();
                }
            }

            textDefinition_clean = Utils.PurgeText(textDefinition);
        }

        public bool IsQuotedIdentifier { get; set; }
        public bool IsAnsiNulls { get; set; }

        public string TextDefinitionClean {
            get { return textDefinition_clean; }
        }

        public string TextDefinition {
            get { return textDefinition; }
            set { textDefinition = value; }
        }

        protected override bool LocalCompare(DatabaseObject obj) {
            if (obj is StoredProc) {
                if (compareAnsiNullsAndQuotedIdent) {
                    return
                        this.textDefinition_clean == ((StoredProc)obj).textDefinition_clean &&
                        ((StoredProc)obj).IsQuotedIdentifier == this.IsQuotedIdentifier &&
                        ((StoredProc)obj).IsAnsiNulls == this.IsAnsiNulls;
                } else {
                    return
                        this.textDefinition_clean == ((StoredProc)obj).textDefinition_clean;
                }
            }
            return false;
        }

    }

    public class Database {

        #region members

        private string connString;


        #endregion

        public Database(string connString) {
            this.connString = connString;
            this.UserTables = new Dictionary<string, UserTable>();
            this.Views = new Dictionary<string, View>();
            this.StoredProcs = new Dictionary<string, StoredProc>();
            this.Functions = new Dictionary<string, Function>();
            this.Defaults = new Dictionary<string, Constraint>();
            this.CheckConstraints = new Dictionary<string, Constraint>();
            this.Indexes = new Dictionary<string, Index>();
            this.Version = 0;
        }

        #region properties

        public Dictionary<string, UserTable> UserTables { get; set; }
        public Dictionary<string, View> Views { get; set; }
        public Dictionary<string, StoredProc> StoredProcs { get; set; }
        public Dictionary<string, Function> Functions { get; set; }
        public Dictionary<string, Constraint> Defaults { get; set; }
        public Dictionary<string, Constraint> CheckConstraints { get; set; }
        public Dictionary<string, Index> Indexes { get; set; }
        public int Version { get; set; }


        #endregion

        public bool TestConnection() {
            try {
                using (var conn = new SqlConnection(connString)) {
                    conn.Open();
                    using (var command = conn.CreateCommand()) {
                        command.CommandText = "select count(*) from sysobjects";
                        object o = command.ExecuteScalar();
                        if (o == null || o == DBNull.Value)
                            return false;
                        int i = (int)o;
                        if (i <= 0)
                            return false;
                    }

                    using (var command = conn.CreateCommand()) {
                        command.CommandText = "SELECT SERVERPROPERTY('ProductVersion') AS [Version]";
                        object o = command.ExecuteScalar();
                        if (o == null || o == DBNull.Value) {
                            Version = 0;
                        } else {
                            int dversion = Convert.ToInt32(((string)o).Substring(0, ((string)o).IndexOf('.')));
                            Version = dversion;
                        }
                    }

                    conn.Close();
                }
            } catch {
                return false;
            }
            return true;
        }

        public void LoadObjects(bool use_ansi_and_quoted_ident) {
            try {
                using (var conn = new SqlConnection(connString)) {
                    conn.Open();
                    using (var command = conn.CreateCommand()) {
                        command.CommandText = "select name, id from sysobjects where xtype='U' and not name like 'sys%'";
                        using (var reader = command.ExecuteReader()) {
                            while (reader.Read()) {
                                var t = new UserTable(reader.GetString(0), reader.GetInt32(1));
                                UserTables[t.Name] = t;
                            }
                        }
                        command.CommandText = "select name, id from sysobjects where xtype='V' and category=0 and not name like 'sys%'";
                        using (var reader = command.ExecuteReader()) {
                            while (reader.Read()) {
                                var v = new View(reader.GetString(0), reader.GetInt32(1), use_ansi_and_quoted_ident);
                                Views[v.Name] = v;
                            }
                        }
                        command.CommandText = "select name, id from sysobjects where xtype='P' and category=0 and not name like 'sp%'";
                        using (var reader = command.ExecuteReader()) {
                            while (reader.Read()) {
                                var sp = new StoredProc(reader.GetString(0), reader.GetInt32(1), use_ansi_and_quoted_ident);
                                StoredProcs[sp.Name] = sp;
                            }
                        }
                        command.CommandText = "select name, id from sysobjects where xtype IN ('FN', 'IF', 'TF') and category=0 and not name like 'fn%'";
                        using (var reader = command.ExecuteReader()) {
                            while (reader.Read()) {
                                var f = new Function(reader.GetString(0), reader.GetInt32(1), use_ansi_and_quoted_ident);
                                Functions[f.Name] = f;
                            }
                        }

                        command.CommandText = @"
                            SELECT c_obj.name as [constraint_name], c_obj.id  as id
                            FROM sysobjects t_obj, sysobjects c_obj, syscolumns cols
                            WHERE cols.id = t_obj.id AND c_obj.id = cols.cdefault AND c_obj.xtype = 'D'
                            ";
                        using (var reader = command.ExecuteReader()) {
                            while (reader.Read()) {
                                var f = new Constraint(reader.GetString(0), reader.GetInt32(1), ConstraintType.DefaultConstraint);
                                Defaults[f.Name] = f;
                            }
                        }

                        command.CommandText = "select constraint_name, object_id(constraint_name) from information_schema.TABLE_CONSTRAINTS";
                        using (var reader = command.ExecuteReader()) {
                            while (reader.Read()) {
                                var f = new Constraint(reader.GetString(0), reader.GetInt32(1), ConstraintType.CheckConstraint);
                                CheckConstraints[f.Name] = f;
                            }
                        }

                        command.CommandText = @"
                            select 
                                i.name as index_name,
                                i.id as id,
                                cast(i.indid as int) as indid
                            from
                                sysindexes i
                                inner join sysindexkeys ik on ik.id = i.id and ik.indid = i.indid
                                inner join syscolumns c on c.id = ik.id and c.colid =  ik.colid
                            where
                                not i.name like '_WA_Sys%' and
                                not object_name(i.id) like 'sys%' 
                            ";
                        using (var reader = command.ExecuteReader()) {
                            while (reader.Read()) {
                                var f = new Index(reader.GetString(0), reader.GetInt32(1), reader.GetInt32(2));
                                Indexes[f.Name] = f;
                            }
                        }

                    }
                    // gather the data for the tables, views, procs and functions
                    foreach (UserTable t in UserTables.Values) {
                        t.GatherData(conn);
                    }
                    foreach (View v in Views.Values)
                        v.GatherData(conn);
                    foreach (StoredProc sp in StoredProcs.Values)
                        sp.GatherData(conn);
                    foreach (Function f in Functions.Values)
                        f.GatherData(conn);

                    foreach (Constraint f in Defaults.Values)
                        f.GatherData(conn);
                    foreach (Constraint f in CheckConstraints.Values)
                        f.GatherData(conn);

                    foreach (Index f in Indexes.Values)
                        f.GatherData(conn);

                    conn.Close();
                }
            } catch (Exception ex) {
                throw ex;
            }
        }

        public List<DBDifference> CompareTo(Database db2) {
            var diffs = new List<DBDifference>();
            CompareObjects(this.UserTables, db2.UserTables, "Table", diffs);
            CompareObjects(this.Views, db2.Views, "View", diffs);
            CompareObjects(this.StoredProcs, db2.StoredProcs, "StoredProc", diffs);
            CompareObjects(this.Functions, db2.Functions, "Function", diffs);
            CompareObjects(this.Defaults, db2.Defaults, "Default", diffs);
            CompareObjects(this.CheckConstraints, db2.CheckConstraints, "Constraint", diffs);
            CompareObjects(this.Indexes, db2.Indexes, "Index", diffs);
            return diffs;
        }

        private void CompareObjects<T>(IDictionary<string, T> ht1, IDictionary<string, T> ht2, string type, List<DBDifference> diffs) where T : DatabaseObject {
            foreach (var t in ht1.Values) {
                if (!ht2.ContainsKey(t.Name)) {
                    diffs.Add(new DBDifference(type, t.Name, "Missing in Database 2"));
                }
            }
            foreach (var t in ht2.Values) {
                if (!ht1.ContainsKey(t.Name)) {
                    diffs.Add(new DBDifference(type, t.Name, "Missing in Database 1"));
                }
            }
            foreach (var t in ht1.Values) {
                if (!ht2.ContainsKey(t.Name)) continue;
                var o = ht2[t.Name];
                if (!t.CompareTo(o)) {
                    diffs.Add(new DBDifference(type, t.Name, "Different"));
                }
            }
        }
    }

    public class DBDifference : IComparable {
        string type;
        string name;
        string status;

        public DBDifference(string type, string name, string status) {
            this.type = type;
            this.name = name;
            this.status = status;
        }

        public string Type {
            get { return type; }
            set { type = value; }
        }

        public string Name {
            get { return name; }
            set { name = value; }
        }

        public string Status {
            get { return status; }
            set { status = value; }
        }
        #region IComparable Members

        public int CompareTo(object obj) {
            DBDifference d = obj as DBDifference;
            if (d != null) {
                if (d.Type != this.Type) {
                    return GetOrderOfTypes(this.Type).CompareTo(GetOrderOfTypes(d.Type));
                }
                return this.Name.CompareTo(d.Name);
            }
            return 0;
        }

        private int GetOrderOfTypes(string type) {
            switch (type) {
                case "Table": return 0;
                case "View": return 1;
                case "Function": return 2;
                case "Procedure": return 3;
                case "StoredProc": return 3;
                case "Index": return 4;
                case "Default": return 5;
                case "Constraint": return 6;
                default: return 7;
            }
        }

        #endregion
    }
}
