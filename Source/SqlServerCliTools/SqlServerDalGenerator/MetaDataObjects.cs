using System;
using System.Data;
using System.Data.SqlClient;
using System.Collections.Generic;
using System.Linq;

namespace DalGenerator {

    #region base class
    //////////////////////////////////////////////////////////////////////////
    /// <summary>Base class for our objects.
    /// Has a name and can be sorted. </summary>

    public class LowercasedNamedObjectX : IComparable {

        /// <summary>
        /// Name of this object
        /// </summary>
        string name;

        /// <summary>This is the public accessor to name of this object.
        /// Enforces uppercase.</summary>

        public string Name
        {
            set { name = value.ToLower(); }
            get { return name; }
        }

        /// <summary>static member to be used for nice output.</summary>

        public static DBCompare_DBTypeMap type_map;

        /// <summary>Constructor. Initializes staticv member, if needed.</summary>

        public LowercasedNamedObjectX() {
            if (type_map == null) type_map = new DBCompare_DBTypeMap();
        }

        /// <summary>Implementation of IComparable. This is needed for sorting.</summary>

        int IComparable.CompareTo(object obj) {
            return Name.CompareTo(((LowercasedNamedObjectX)obj).Name);
        }
    }
    //////////////////////////////////////////////////////////////////////////
    /// <summary>Base class for our objects.
    /// Has a name and can be sorted. </summary>

    public class NamedObject : IComparable {

        /// <summary>
        /// Name of this object
        /// </summary>
        string name;

        /// <summary>This is the public accessor to name of this object.
        /// Enforces uppercase.</summary>

        public string Name
        {
            set { name = value/*.ToLower()*/; }
            get { return name; }
        }

        /// <summary>static member to be used for nice output.</summary>

        public static DBCompare_DBTypeMap type_map;

        /// <summary>Constructor. Initializes staticv member, if needed.</summary>

        public NamedObject() {
            if (type_map == null) type_map = new DBCompare_DBTypeMap();
        }

        /// <summary>Implementation of IComparable. This is needed for sorting.</summary>

        int IComparable.CompareTo(object obj) {
            return Name.CompareTo(((NamedObject)obj).Name);
        }
    }

    #endregion

    #region tables

    //////////////////////////////////////////////////////////////////////////
    /// <summary>
    /// This class represents database field
    /// </summary>
    public class DBField : NamedObject {

        public string NameInCode { get; set; }

        /// <summary> Database type </summary>
        public string dbtype;

        /// <summary> Field length </summary>
        public Int64 length = 0;

        /// <summary> Field precision</summary>
        public int precision = 0;

        /// <summary> Field scale</summary>
        public int scale = 0;

        /// <summary> Flag if field is readonly </summary>
        public bool is_readonly = false;

        /// <summary> Flag if field is identity </summary>
        public bool is_identity = false;

        /// <summary> Flag if field is key </summary>
        public bool is_key = false;

        /// <summary> Flag if field is nullable </summary>
        public bool is_nullable = false;

        /// <summary> Default value - expression </summary>
        public string default_value = null;

        /// <summary> Default value - expression </summary>
        public bool is_computed = false;

        string[] char_types = "char,nchar,varchar,nvarchar,text,ntext".Split(',');

        /// <summary>
        /// Public constructor
        /// </summary>
        public DBField(string n, string t, Int64 l, int prec, int sc, bool ident, bool key, bool nullable, string df, bool comp) {
            Name = n;
            dbtype = t;
            this.precision = prec;
            this.scale = sc;
            length = l;
            is_identity = ident;
            is_key = key;
            is_nullable = nullable;
            default_value = (df == null || df == "" ? (string)null : df.Trim());
            is_computed = comp;

            // make corrections to default values
            if (default_value != null) {
                if (t == "bit") {
                    default_value = default_value.Replace("(", "").Replace(")", "").Replace("1", "true").Replace("0", "false");
                } else if (Array.IndexOf(char_types, t) >= 0) {
                    if (default_value.StartsWith("N")) {
                        default_value = default_value.Substring(1);
                    }
                    if (default_value.StartsWith("'") && default_value.EndsWith("'")) {
                        int len = default_value.Length;
                        string inner_string = default_value.Substring(1).Substring(0, len - 2).Trim();
                        default_value = '"' + inner_string + '"';
                        if (!is_nullable && length == 1) {
                            if (inner_string.Length == 0)
                                inner_string = " ";
                            default_value = '\'' + inner_string + '\'';
                        }
                    }
                } else if (t.IndexOf("date") >= 0) {
                    if (default_value.StartsWith("'")) {
                        default_value = default_value.Replace("'", "");
                        default_value = "SqlDateTime.Parse(\"" + default_value + "\").Value";
                    } else {
                        default_value = default_value.Replace("getdate()", "DateTime.Now");
                    }
                } else if (t.IndexOf("decimal") >= 0) {
                    default_value = default_value.Replace("(", "").Replace(")", "") + "m";
                } else {
                    default_value = default_value.Replace("(", "").Replace(")", "").Replace("@@spid", "-1");
                }
            } else {
                //if (t == "datetime" && is_nullable)
                //    default_value = "SqlDateTime.Null";
            }
            is_readonly = (t == "timestamp") || ident || comp;
        }
    }

    //////////////////////////////////////////////////////////////////////////

    /// <summary>
    /// This class represent database table
    /// </summary>
    public class DBTable : NamedObject {

        public string ClassName { get; set; }
        public bool WasGenerated { get; set; }

        /// <summary>
        /// Array of fields inside the table
        /// </summary>
        List<DBField> fields = new List<DBField>();

        public List<DBField> Fields
        {
            get { return fields; }
        }

        /// <summary>
        /// Flag is this table is actually a view
        /// </summary>
        public readonly bool is_table;

        /// <summary>
        /// Public constructor
        /// </summary>
        public DBTable() { }

        /// <summary>
        /// Public constructor with initialization parameters
        /// </summary>
        public DBTable(SqlConnection con, string table_name, bool is_table) {
            Name = table_name;
            Load(con, is_table);
            this.is_table = is_table;
            this.WasGenerated = false;
        }

        /// <summary>
        /// This method adds field.
        /// </summary>
        /// <param name="f"></param>
        public void AddField(DBField f) { fields.Add(f); fields.Sort(); }

        /// <summary>
        /// This method returns field count
        /// </summary>
        /// <returns></returns>
        public int FieldCount() { return fields.Count; }

        /// <summary>
        /// This method finds field by its name
        /// </summary>
        public DBField FindFieldByName(string name) {
            string s = name/*.ToUpper()*/;
            foreach (DBField field in fields)
                if (field.Name == s)
                    return field;
            return null;
        }

        /// <summary>
        /// This method loads table meta-data from specified connection
        /// </summary>
        public void Load(SqlConnection con, bool is_table) {
            if (Name == "") throw new Exception("Could not load table info - table name not set yet.");

            DataTable st;
            if (is_table)
                st = Utils.GetFieldsOfTable(con, Name);
            else
                st = Utils.GetFieldsOfView(con, Name);

            fields.Clear();
            foreach (DataRow r in st.Rows) {
                int len = (r.IsNull("char_len") ? -1 : int.Parse(r["char_len"].ToString()));
                int prec = (r.IsNull("precision") ? -1 : int.Parse(r["precision"].ToString()));
                int scale = (r.IsNull("scale") ? -1 : int.Parse(r["scale"].ToString()));
                bool ident = (bool)r["is_identity"];
                bool nullable = (bool)r["is_nullable"];
                bool comp = (bool)r["is_computed"];

                fields.Add(
                           new DBField(
                                       (string)r["column_name"],
                                       (string)r["data_type"],
                                       len,
                                       prec,
                                       scale,
                                       ident,
                                       false,
                                       nullable,
                                       r["column_default"].ToString(),
                                       comp
                                      ));
            }
            fields.Sort();

            if (is_table) {
                DataTable keys = Utils.GetPrimaryKeysForTable(con, Name);
                foreach (DataRow r in keys.Rows) {
                    string n = r["column_name"].ToString().ToUpper();
                    foreach (DBField field in fields)
                        if (field.Name.ToUpper() == n)
                            field.is_key = true;
                }
            }
        }
    }

    #endregion

    #region stored procedures

    /// <summary>
    /// This class represents stored-procedure parameter
    /// </summary>
    public class SpParam : NamedObject {

        /// <summary>
        /// Parameter type
        /// </summary>
        public DbType type;

        /// <summary>
        /// Flag if it is output parameter
        /// </summary>
        public bool is_out;

        /// <summary>
        /// C# name - could be different than Name
        /// </summary>
        public string cs_name;

        /// <summary>
        /// Original parameter
        /// </summary>
        public SqlParameter orig_parameter;
    }

    /// <summary>
    /// This class represents stored procedure
    /// </summary>
    public class StoredProcedure : NamedObject {

        public string StoredProcedureDefinition { get; set; }
        public bool StoredProcedureReturnsData { get; set; }

        public DataSet ReturnedData { get; private set; }

        /// <summary> Array of parameters </summary>
        public SpParam[] parameters;

        /// <summary>
        /// This method loads stored procedure meta-data from specified connection
        /// </summary>
        public void Load(SqlConnection con, SqlTransaction trans, CodeGenerationParameters cgp) {
            if (Name == "")
                throw new Exception("Could not load SP info - name not set yet.");

            SqlCommand cmd = Utils.GetSpParameters(con, trans, Name);

            var ar = new List<SpParam>();
            var ar_exec_list = new List<string>();
            foreach (SqlParameter par_x in cmd.Parameters) {
                if (par_x.ParameterName.ToUpper() == "@RETURN_VALUE") continue;
                SpParam par = new SpParam();
                par.Name = par_x.ParameterName.Replace("@", "");
                par.is_out = (par_x.Direction == ParameterDirection.Output || par_x.Direction == ParameterDirection.InputOutput);
                par.type = par_x.DbType;
                par.orig_parameter = par_x;
                ar.Add(par);
                switch (par_x.SqlDbType) {
                    case SqlDbType.UniqueIdentifier:
                        ar_exec_list.Add("null");
                        break;
                    case SqlDbType.Bit:
                        ar_exec_list.Add("1");
                        break;
                    case SqlDbType.Char:
                    case SqlDbType.VarChar:
                    case SqlDbType.Text:
                    case SqlDbType.NChar:
                    case SqlDbType.NVarChar:
                    case SqlDbType.NText:
                        ar_exec_list.Add("''");
                        break;
                    case SqlDbType.DateTime:
                        ar_exec_list.Add("'20090101'");
                        break;
                    case SqlDbType.Decimal:
                    case SqlDbType.Int:
                    case SqlDbType.SmallInt:
                    case SqlDbType.TinyInt:
                    case SqlDbType.BigInt:
                        ar_exec_list.Add("1");
                        break;
                    default:
                        ar_exec_list.Add("null");
                        Console.WriteLine(string.Format("{0} {1}", par_x.DbType, par_x.SqlDbType));
                        break;
                }
            }
            parameters = ar.ToArray();


            var sql = "SELECT routine_definition FROM information_schema.routines WHERE specific_name  = '" + this.Name + "'";
            cmd = new SqlCommand(sql, con, trans);
            //System.Diagnostics.Trace.WriteLine(sql);
            var ds = Utils.GetDataSet(cmd);
            var dt = ds.Tables[0];
            this.StoredProcedureDefinition = dt.Rows[0][0].ToString();

            if (this.StoredProcedureReturnsData) {
                if (
                    cgp.sp_data_wrapper_flag == null ||
                    cgp.sp_data_wrapper_flag.Trim() == "" ||
                    this.StoredProcedureDefinition.Contains("-- " + cgp.sp_data_wrapper_flag)
                    ) {
                    sql = "exec dbo." + this.Name;
                    //if (ar_exec_list.Count > 0) {
                    //    sql += " " + string.Join(", ", ar_exec_list.ToArray());
                    //}
                    var cmd2 = new SqlCommand(sql, con, trans);
                    System.Diagnostics.Trace.WriteLine(sql);
                    this.ReturnedData = Utils.GetDataSet(cmd2);
                }
            }
        }
    }

    #endregion
}
