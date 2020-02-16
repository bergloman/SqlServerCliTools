using System;
using System.Web;
using System.Data;
using Microsoft.Data.SqlClient;
using System.Data.SqlTypes;
using System.Xml;
using System.Xml.Serialization;
using System.IO;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

namespace DalGenerator {

    static class Consts {
        public const string TableAttr = "DataTable";
        public const string FieldAttr = "DataField";
    }

    #region type mapping

    ///////////////////////////////////////////////////////////////////////
    /// <summary>
    /// This class is used as a mapping from DBType integers to their
    /// string description.
    /// </summary>
    ///////////////////////////////////////////////////////////////////////

    public class DBCompare_DBTypeMap {

        /// <summary>
        /// This array stores invalid names (C# keywords)
        /// </summary>
        public readonly string[] invalid_names;

        /// <summary>Internal member of this class. Lists types that are unicode.</summary>
        SortedList<string, string> map_tab_unicode = new SortedList<string, string>();

        /// <summary>Internal member of this class. Maps type codes to descriptions.</summary>
        Dictionary<string, string> map_tab = new Dictionary<string, string>();

        /// <summary>Internal member of this class. Maps C# types in DataTable to wrapper member types.</summary>
        Dictionary<Type, string> map_tab_sp = new Dictionary<Type, string>();

        /// <summary>Internal member of this class. Maps type codes to descriptions.</summary>
        Dictionary<string, string> map_xsd = new Dictionary<string, string>();

        /// <summary>Internal member of this class. Maps type codes to descriptions.</summary>
        Dictionary<DbType, string> map_sp = new Dictionary<DbType, string>();

        /// <summary>Internal member of this class. Maps type codes to descriptions.</summary>
        Dictionary<DbType, string> map_sp_out = new Dictionary<DbType, string>();

        /// <summary>Constrcutor. Fills the map variable with values.</summary>
        public DBCompare_DBTypeMap() {
            invalid_names = "abstract,event,new,struct,as,explicit,null,switch,base,extern,object,this,bool,false,operator,throw,break,finally,out,true,byte,fixed,override,try,case,float,params,typeof,catch,for,private,uint,char,foreach,protected,ulong,checked,goto,public,unchecked,class,if,readonly,unsafe,const,implicit,ref,ushort,continue,in,return,using,decimal,int,sbyte,virtual,default,interface,sealed,volatile,delegate,internal,short,void,do,is,sizeof,whiledouble,lock,stackalloc,else,long,static,enum,namespace,string".Split(',');
            Array.Sort(invalid_names);

            map_tab_unicode.Add("nchar", "nchar");
            map_tab_unicode.Add("null-nchar", "null-nchar");
            map_tab_unicode.Add("ntext", "ntext");
            map_tab_unicode.Add("null-ntext", "null-ntext");
            map_tab_unicode.Add("nvarchar", "nvarchar");
            map_tab_unicode.Add("null-nvarchar", "null-nvarchar");

            map_tab.Add("bigint", "long");
            map_tab.Add("null-bigint", "long?");
            map_tab.Add("binary", "byte[]");
            map_tab.Add("null-binary", "byte[]");
            map_tab.Add("bit", "bool");
            map_tab.Add("null-bit", "bool?");
            map_tab.Add("char", "string");
            map_tab.Add("null-char", "string");
            map_tab.Add("date", "DateTime");
            map_tab.Add("null-date", "DateTime?");
            map_tab.Add("datetime", "DateTime");
            map_tab.Add("null-datetime", "DateTime?");
            map_tab.Add("decimal", "decimal");
            map_tab.Add("null-decimal", "decimal?");
            map_tab.Add("float", "double");
            map_tab.Add("null-float", "double?");
            map_tab.Add("image", "byte[]");
            map_tab.Add("null-image", "byte[]");
            map_tab.Add("int", "int");
            map_tab.Add("null-int", "int?");
            map_tab.Add("money", "decimal");
            map_tab.Add("null-money", "decimal?");
            map_tab.Add("nchar", "string");
            map_tab.Add("null-nchar", "string");
            map_tab.Add("numeric", "decimal");
            map_tab.Add("null-numeric", "decimal?");
            map_tab.Add("ntext", "string");
            map_tab.Add("null-ntext", "string");
            map_tab.Add("nvarchar", "string");
            map_tab.Add("null-nvarchar", "string");
            map_tab.Add("smalldatetime", "DateTime");
            map_tab.Add("null-smalldatetime", "DateTime?");
            map_tab.Add("smallint", "short");
            map_tab.Add("null-smallint", "short?");
            map_tab.Add("text", "string");
            map_tab.Add("null-text", "string");
            map_tab.Add("timestamp", "byte[]");
            map_tab.Add("null-timestamp", "byte[]");
            map_tab.Add("tinyint", "byte");
            map_tab.Add("null-tinyint", "byte?");
            map_tab.Add("uniqueidentifier", "System.Guid");
            map_tab.Add("null-uniqueidentifier", "Guid?");
            map_tab.Add("varbinary", "byte[]");
            map_tab.Add("null-varbinary", "byte[]");
            map_tab.Add("varchar", "string");
            map_tab.Add("null-varchar", "string");
            map_tab.Add("xml", "string");
            map_tab.Add("null-xml", "string");
            map_tab.Add("sql_variant", "object");
            map_tab.Add("null-sql_variant", "object");




            map_sp.Add(DbType.Int64, "long?");
            map_sp.Add(DbType.Binary, "byte[]");
            map_sp.Add(DbType.Boolean, "bool?");
            map_sp.Add(DbType.AnsiStringFixedLength, "string");
            map_sp.Add(DbType.DateTime, "DateTime?");
            map_sp.Add(DbType.Date, "DateTime?");
            map_sp.Add(DbType.Decimal, "decimal?");
            map_sp.Add(DbType.Double, "double?");
            //map_sp.Add(DbType.Binary, "byte[]");
            map_sp.Add(DbType.Int32, "int?");
            map_sp.Add(DbType.Currency, "decimal?");
            map_sp.Add(DbType.StringFixedLength, "string");
            //map_sp.Add(DbType.NText, "string");
            map_sp.Add(DbType.String, "string");
            map_sp.Add(DbType.Single, "Single?");
            //map_sp.Add(DbType.DateTime, "DateTime?");
            map_sp.Add(DbType.Int16, "short?");
            //map_sp.Add(DbType.Currency, "decimal?");
            //map_sp.Add(DbType., "string");
            //map_sp.Add(DbType.Int64, "byte[]");
            map_sp.Add(DbType.Byte, "byte?");
            map_sp.Add(DbType.Guid, "Guid?");
            map_sp.Add(DbType.AnsiString, "string");
            map_sp.Add(DbType.Xml, "string");

            map_sp_out.Add(DbType.Int64, "Int64");
            map_sp_out.Add(DbType.Binary, "byte[]");
            map_sp_out.Add(DbType.Boolean, "bool");
            map_sp_out.Add(DbType.AnsiStringFixedLength, "string");
            map_sp_out.Add(DbType.DateTime, "DateTime");
            map_sp_out.Add(DbType.Date, "DateTime");
            map_sp_out.Add(DbType.Decimal, "decimal");
            map_sp_out.Add(DbType.Double, "double");
            //map_sp_out.Add(DbType.Image, "byte[]");
            map_sp_out.Add(DbType.Int32, "int");
            map_sp_out.Add(DbType.Currency, "decimal");
            map_sp_out.Add(DbType.StringFixedLength, "string");
            //map_sp_out.Add(DbType.NText, "string");
            map_sp_out.Add(DbType.String, "string");
            map_sp_out.Add(DbType.Single, "Single");
            //map_sp_out.Add(DbType.SmallDateTime, "DateTime");
            map_sp_out.Add(DbType.Int16, "short");
            //map_sp_out.Add(DbType.SmallMoney, "decimal");
            //map_sp_out.Add(DbType.Text, "string");
            //map_sp_out.Add(DbType.Timestamp, "byte[]");
            map_sp_out.Add(DbType.Byte, "byte");
            map_sp_out.Add(DbType.Guid, "Guid");
            //map_sp_out.Add(SqlDbType.VarBinary, "byte[]");
            map_sp_out.Add(DbType.AnsiString, "string");
            //map_sp_out.Add(DbType.Variant, "object");
            map_sp_out.Add(DbType.Xml, "string");

            map_tab_sp.Add(typeof(int), "int?");
            map_tab_sp.Add(typeof(Int16), "Int16?");
            map_tab_sp.Add(typeof(Int64), "Int64?");
            map_tab_sp.Add(typeof(string), "string");
            map_tab_sp.Add(typeof(DateTime), "DateTime?");
            map_tab_sp.Add(typeof(bool), "bool?");
            map_tab_sp.Add(typeof(uint), "uint?");
            map_tab_sp.Add(typeof(byte), "byte?");
            map_tab_sp.Add(typeof(Guid), "Guid?");
            map_tab_sp.Add(typeof(float), "float?");
            map_tab_sp.Add(typeof(double), "double?");
            map_tab_sp.Add(typeof(decimal), "decimal?");
        }

        /// <summary>Main method of this object.
        /// Used for retrieving descriptions of types. </summary>
        public string GetDesc(string index, bool nullable, long len) {
            string n = (nullable ? "null-" : "") + index;
            if (!map_tab.ContainsKey(n)) return "!!error!!";
            string res = map_tab[n];
            if (!nullable && len == 1 && res == "string")
                res = "char";
            return res;
        }

        /// <summary>Main method of this object.
        /// Used for retrieving descriptions of types. </summary>
        public string GetDescCs(Type t) {
            if (!map_tab_sp.ContainsKey(t))
                if (t.IsArray)
                    return t.Name;
                else
                    return t.Name + "?";
            var res = map_tab_sp[t];
            return res;
        }

        /// <summary>Main method of this object.
        /// Used for retrieving descriptions of types. </summary>
        public string GetDescXsd(string index, bool nullable, long len) {
            string n = (nullable ? "null-" : "") + index;
            if (!map_xsd.ContainsKey(n)) return "!!error!!";
            string res = map_xsd[n];
            return res;
        }

        /// <summary>Main method of this object.
        /// Used for retrieving stored procedure parameter types. </summary>
        public string GetSpParamType(DbType type, bool for_out) {
            if (!map_sp.ContainsKey(type)) return "!!error!!";
            if (for_out)
                return map_sp_out[type];
            return map_sp[type];
        }

        /// <summary>
        /// This method tests if specified type is unicode type.
        /// </summary>
        public bool IsUnicodeType(string t) {
            return map_tab_unicode.ContainsKey(t);
        }
    }

    #endregion

    //////////////////////////////////////////////////////////////////////////
    /// <summary>
    /// This class generates code for DAL
    /// </summary>
    public class CodeGenerator {

        #region members

        /// <summary>
        /// Input parameters
        /// </summary>
        CodeGenerationParameters pars;

        /// <summary>
        /// Sql connection for db access
        /// </summary>
        SqlConnection con;

        /// <summary>
        /// Indentation constant
        /// </summary>
        readonly string indent = "    ";

        /// <summary>
        /// Type-mapping helper
        /// </summary>
        readonly DBCompare_DBTypeMap type_map = new DBCompare_DBTypeMap();

        #endregion

        #region logging

        /// <summary>
        /// This method sends given message to listener
        /// </summary>
        /// <param name="msg"></param>
        void Log(string msg) {
            Console.WriteLine(msg);
        }

        #endregion

        #region public methods

        /// <summary>
        /// Public constructor
        /// </summary>
        public CodeGenerator(CodeGenerationParameters pars) {
            this.pars = pars;
        }

        /// <summary>
        /// Main method of this class
        /// </summary>
        public void CreateCsCode() {
            var sb = new StringBuilder();
            if (pars.use_username) {
                con = Utils.CreateConnection(pars.sql_server_name, pars.schema_name, pars.uid, pars.pwd);
            } else {
                con = Utils.CreateConnection(pars.sql_server_name, pars.schema_name);
            }
            try {
                GenerateNamespaces(sb);
                sb.AppendLine("namespace " + pars.code_namespace + " {");

                // insert optional code that user specified in settings
                if (pars.namespace_start_code != null) {
                    sb.AppendLine(pars.namespace_start_code);
                    sb.AppendLine();
                }

                if (pars.excluded_tables != null) {
                    for (int i = 0; i < pars.excluded_tables.Length; i++)
                        pars.excluded_tables[i] = pars.excluded_tables[i].ToUpper().Trim();
                }
                if (pars.excluded_tables_prefix != null)
                    pars.excluded_tables_prefix = pars.excluded_tables_prefix.ToUpper();

                if (!pars.dont_inject_infrastructure_classes) {
                    InsertAttributeDefinitions(sb, pars.generate_query_object);
                }
                if (!pars.dont_inject_basic_sql_provider) {
                    InsertBasicProvider(sb);
                }
                if (pars.generate_query_object) {
                    var s = Utils.LoadFileRelative("clips/query_obj.txt");
                    sb.Append(s);
                }

                GenerateCodeForTables(sb);
                GenerateCodeForViews(sb);
                GenerateCodeForSps(sb);

                if (!pars.dont_inject_linq_helpers) {
                    GenerateCodeForDbContext(sb);
                }

                sb.AppendLine("}");

            } finally {
                con.Close();
            }

            using (TextWriter tw = new StreamWriter(pars.export_file_name)) {
                tw.Write(sb.ToString());
            }
        }

        #endregion

        #region utility methods

        /// <summary>
        /// This method creates valid name for C# class or member
        /// </summary>
        string CreateValidCsName(string s) {
            if (Array.BinarySearch(type_map.invalid_names, s) >= 0)
                return "_" + s;
            return s;
        }

        /// <summary>
        /// This method generates code that imports specified namespaces
        /// </summary>
        void GenerateNamespaces(StringBuilder sb) {
            Log("Generating namespaces...");
            var s = Utils.LoadFileRelative("clips/namespaces.txt");
            sb.Append(s);

            // additional namespaces to be included
            if (pars.additional_namespaces != null) {
                foreach (string ns in pars.additional_namespaces) {
                    var nsx = ns.Trim();
                    if (nsx.Length > 0) {
                        sb.AppendLine("using " + nsx + ";");
                    }
                }
            }

            sb.AppendLine();
        }

        /// <summary>
        /// This method generates attribute declarations
        /// </summary>
        void InsertAttributeDefinitions(StringBuilder sb, bool generate_query) {
            Log("Inserting attribute definitions...");
            var s = Utils.LoadFileRelative("clips/attributes.txt");
            string s2 = "";
            if (generate_query) {
                s2 = Utils.LoadFileRelative("clips/attributes_query.txt");
            }
            s = s.Replace("#$#QUERY_HELPER#$#", s2);
            sb.AppendLine(s);
        }

        /// <summary>
        /// This method generates basic SQL-server provider
        /// </summary>
        void InsertBasicProvider(StringBuilder sb) {
            Log("Inserting basic provider definitions...");
            var s = Utils.LoadFileRelative("clips/basic_provider.txt");
            sb.AppendLine(s);
        }

        #endregion

        #region tables

        List<DBTable> table_and_views = new List<DBTable>();

        void GenerateCodeForDbContext(StringBuilder sb) {
            Log("Generating db-context...");
            sb.AppendLine(indent + "#region db context");
            sb.AppendLine();
            sb.AppendLine(indent + "public interface IDbContext {");

            var sb2 = new StringBuilder();
            sb2.AppendLine(indent + "partial class DbContext : IDbContext {");
            sb2.AppendLine(indent + indent + "private IQToolkit.IEntityProvider provider;");
            sb2.AppendLine(indent + indent + "public DbContext(IQToolkit.IEntityProvider provider) { this.provider = provider; }");

            var sb3 = new StringBuilder();
            sb3.AppendLine(indent + "public partial class DbContextMemory : IDbContext {");


            foreach (var tab in table_and_views) {
                if (!tab.WasGenerated)
                    continue;

                sb.AppendLine(indent + indent + "IQueryable<" + tab.ClassName + "> " + tab.Name + " { get; }");

                sb2.AppendLine();
                sb2.AppendLine(indent + indent + "[IQToolkit.Data.Mapping.Table(Name=\"dbo." + tab.Name + "\")]");
                foreach (var field in tab.Fields) {
                    sb2.AppendLine(indent + indent + "[IQToolkit.Data.Mapping.Column(Member=\"" + field.NameInCode + "\"" + (field.is_key ? ", IsPrimaryKey = true" : "") + ")]");
                }

                sb2.AppendLine(indent + indent + "public IQueryable<" + tab.ClassName + "> " + tab.Name + " { get { return this.provider.GetTable<" + tab.ClassName + ">(\"" + tab.Name + "\"); } }");
                sb3.AppendLine();
                sb3.AppendLine(indent + indent + "public List<" + tab.ClassName + "> Data_" + tab.ClassName + " = new List<" + tab.ClassName + ">();");
                sb3.AppendLine(indent + indent + "public IQueryable<" + tab.ClassName + "> " + tab.Name + " { get { return this.Data_" + tab.ClassName + ".AsQueryable(); } }");
            }
            sb.AppendLine(indent + "}");
            sb2.AppendLine(indent + "}");
            sb3.AppendLine(indent + "}");

            sb.AppendLine();
            sb.AppendLine(sb2.ToString());
            sb.AppendLine();

            sb.AppendLine();
            sb.AppendLine(sb3.ToString());
            sb.AppendLine();

            sb.AppendLine(indent + "#endregion");
        }

        /// <summary>
        /// This method generates code for tables
        /// </summary>
        void GenerateCodeForTables(StringBuilder sb) {
            Log("Generating code for tables...");
            sb.AppendLine(indent + "#region database tables");

            // if no tables are specified, select all tables
            if (pars.selected_tables == null || pars.selected_tables.Length == 0) {
                var tab = Utils.GetTables(con);
                var ar = new List<string>();

                foreach (DataRow r in tab.Rows) {
                    ar.Add(r["table_name"].ToString());
                }

                pars.selected_tables = ar.ToArray();
            }

            foreach (string s in pars.selected_tables) {
                Log("Generating table " + s);
                var tab = new DBTable(con, s, true);
                sb.Append(GenerateCodeForTable(pars, tab));
                sb.AppendLine();
                sb.AppendLine();
            }
            sb.AppendLine(indent + "#endregion");
        }

        /// <summary>
        /// This method generates code for views
        /// </summary>
        void GenerateCodeForViews(StringBuilder sb) {
            Log("Generating code for views...");
            sb.AppendLine(indent + "#region database views");

            var views = Utils.GetViews(con);
            ArrayList ar = new ArrayList();

            foreach (DataRow r in views.Rows) {
                ar.Add(r["table_name"]);
            }

            pars.selected_tables = (string[])ar.ToArray(typeof(string));

            foreach (string s in pars.selected_tables) {
                Log("Generating view " + s);
                var tab = new DBTable(con, s, false);
                sb.Append(GenerateCodeForTable(pars, tab));
                sb.AppendLine();
                sb.AppendLine();
            }

            sb.AppendLine(indent + "#endregion");
        }

        /// <summary>
        /// This code generates code for single table
        /// </summary>
        string GenerateCodeForTable(CodeGenerationParameters pars, DBTable tab) {

            var name_upper = tab.Name.ToUpper().Trim();
            if (pars.excluded_tables != null && Array.IndexOf<string>(pars.excluded_tables, name_upper) >= 0)
                return "";
            if (pars.excluded_tables_prefix != null && name_upper.StartsWith(pars.excluded_tables_prefix))
                return "";

            var sb = new StringBuilder();
            var sb2 = new StringBuilder();

            var class_name = (tab.is_table ? pars.class_prefix : pars.view_prefix) + Utils.PascalCasing(tab.Name);
            var base_class = " : " + (pars.base_class_for_tables != null && pars.base_class_for_tables != "" ? pars.base_class_for_tables : "object");
            var db_object_type = (tab.is_table ? "table" : "view");

            tab.ClassName = class_name;
            tab.WasGenerated = true;
            table_and_views.Add(tab);

            sb.AppendLine(indent + "/// <summary> Wrapper for " + db_object_type + " " + tab.Name + ". </summary>");
            sb.AppendLine(indent + "[" + Consts.TableAttr + "(\"" + tab.Name + "\")]");
            sb.AppendLine(indent + "public partial class " + class_name + base_class + " {");

            var constructor_code = new List<string>();

            foreach (var field in tab.Fields) {

                var field_name = (pars.make_names_lowercase ? field.Name.ToLower() : field.Name);

                sb.AppendLine("");
                sb.AppendLine(indent + indent + "/// <summary> The value of the corresponding field in " + tab.Name + " " + db_object_type + ". </summary>");
                sb.Append(indent + indent + "[" + Consts.FieldAttr + "(\"" + field_name + "\"");

                var len_str = "";
                if (field.length > 0) {
                    len_str = "(" + field.length + ")";
                } else if (field.dbtype == "decimal") {
                    len_str = "(" + field.precision + "," + field.scale + ")";
                }
                sb.AppendFormat(", DbType=\"{0}{1}\"", field.dbtype, len_str);
                if (field.is_readonly || !tab.is_table) sb.Append(", ReadOnly=true");
                if (field.is_nullable) sb.Append(", Nullable=true");
                if (field.is_identity) sb.Append(", Identity=true");
                if (field.is_key) sb.Append(", Key=true");
                if (field.length >= 0) sb.Append(", MaxLen=" + field.length);
                if (type_map.IsUnicodeType(field.dbtype)) sb.Append(", Unicode=true");

                sb.AppendLine(")]");

                var member_name = CreateValidCsName(pars.member_prefix + field_name);
                var member_type = type_map.GetDesc(field.dbtype, field.is_nullable, field.length);

                field.NameInCode = member_name;

                sb.Append(indent + indent + "public " + member_type + " " + member_name);
                if (field.default_value != null && field.default_value != "") {
                    //sb.Append(" = " + field.default_value);
                    constructor_code.Add(indent + indent + indent + member_name + " = " + field.default_value + ";");
                }
                sb.AppendLine("{ get; set; }");

                sb2.AppendLine(indent + indent + indent + "///<summary>Name of column inside this table</summary>");
                sb2.AppendFormat(indent + indent + indent + "public const string {0} = @\"{0}\";", member_name);
                sb2.AppendLine();
            }

            if (constructor_code.Count > 0) {
                sb.AppendLine();
                sb.AppendLine(indent + indent + "///<summary> Public constructor that sets default values </summary>");
                sb.AppendLine(indent + indent + "public " + class_name + "() {");
                foreach (var ts in constructor_code) {
                    sb.AppendLine(ts);
                }
                sb.AppendLine(indent + indent + "}");
            }

            sb.AppendLine("");
            sb.AppendLine(indent + indent + "#region Columns Struct");
            sb.AppendLine(indent + indent + "///<summary>Columns for this table</summary>");
            sb.AppendLine(indent + indent + "public struct Columns {");
            sb.Append(sb2.ToString());
            sb.AppendLine(indent + indent + "}");
            sb.AppendLine(indent + indent + "#endregion");
            sb.AppendLine();
            sb.AppendLine(indent + indent + "///<summary> Name of this table (in database) </summary>");
            sb.AppendFormat(indent + indent + "public static string TableName = @\"{0}\";", tab.Name);
            sb.AppendLine();

            if (pars.generate_query_object) {
                sb.AppendLine();
                sb.AppendLine(indent + indent + "///<summary> Simple utility function for queries </summary>");
                sb.AppendLine(indent + indent + "public static Query<" + class_name + "> CreateQuery() { return new Query<" + class_name + ">(); }");
            }

            sb.AppendLine(indent + "}");
            return sb.ToString();
        }

        #endregion

        #region stored procedures

        class SpStreamHelper {
            public StringBuilder sb_static = new StringBuilder();
            public StringBuilder sb_intf = new StringBuilder();
            public StringBuilder sb_obj = new StringBuilder();
            public StringBuilder sb_tabs = new StringBuilder();
        }

        /// <summary>
        /// This method generates code for stored procedures
        /// </summary>
        void GenerateCodeForSps(StringBuilder sb_root) {
            if (!pars.use_sps) return;

            Log("Generating code for SPs...");

            var helper = new SpStreamHelper();

            var tab = Utils.GetSPs(con);
            var ar = new List<string>();

            foreach (DataRow r in tab.Rows) {
                ar.Add(r["sp_name"].ToString().ToLower());
            }

            var selected_sps = ar.ToArray();
            string class_name = pars.sp_class_name;

            helper.sb_static.AppendLine(indent + "/// <summary> Public static class containing stored-procedure wrappers. </summary>");
            helper.sb_static.AppendLine(indent + "public static class " + class_name + " {");
            helper.sb_static.Append(Utils.LoadFileRelative("clips/check_nullable.txt"));

            helper.sb_intf.AppendLine(indent + "/// <summary> Public interface containing stored-procedure wrappers. </summary>");
            helper.sb_intf.AppendLine(indent + "public partial interface I" + class_name + " {");

            helper.sb_obj.AppendLine(indent + "/// <summary> Public class containing stored-procedure wrappers. </summary>");
            helper.sb_obj.AppendLine(indent + "public partial class " + class_name + "Obj :  I" + class_name + " {");
            helper.sb_obj.AppendLine("");
            helper.sb_obj.AppendLine(indent + indent + "protected IDataProviderSp provider;");
            helper.sb_obj.AppendLine(indent + indent + "public " + class_name + "Obj(IDataProviderSp _provider) { this.provider = _provider; }");
            helper.sb_obj.AppendLine("");
            helper.sb_obj.Append(Utils.LoadFileRelative("clips/check_nullable.txt"));
            helper.sb_obj.AppendLine("");


            foreach (string sp_name in selected_sps) {

                Log("Generating stored procedure " + sp_name);
                var sp = new StoredProcedure();
                sp.Name = sp_name;
                if (pars.use_sp_with_tables)
                    sp.StoredProcedureReturnsData = sp.Name.StartsWith(pars.prefix_sp_with_tables);

                var con2 = new SqlConnection();
                if (pars.use_username)
                    con2 = Utils.CreateConnection(pars.sql_server_name, pars.schema_name, pars.uid, pars.pwd);
                else
                    con2 = Utils.CreateConnection(pars.sql_server_name, pars.schema_name);
                try {
                    var trans = con2.BeginTransaction();
                    try {
                        sp.Load(con2, trans, pars);
                    } catch (Exception ex) {
                        System.Diagnostics.Trace.WriteLine("Error while executing stored procedure " + sp.Name + ". " + ex.ToString());
                    } finally {
                        trans.Rollback();
                    }
                } finally {
                    con2.Close();
                }

                helper.sb_static.AppendLine("\n\n#region " + sp.Name);
                helper.sb_intf.AppendLine(indent + indent + "#region " + sp.Name);
                helper.sb_obj.AppendLine(indent + indent + "#region " + sp.Name);

                CreateCodeForSp(sp, class_name, helper /*sb2, sb3*/);

                helper.sb_static.AppendLine("\n\n#endregion");
                helper.sb_intf.AppendLine(indent + indent + "#endregion");
                helper.sb_obj.AppendLine(indent + indent + "#endregion");
            }

            helper.sb_static.AppendLine("\n\n#region result-table wrappers");
            helper.sb_static.AppendLine(helper.sb_tabs.ToString()); // append classes into static object
            helper.sb_static.AppendLine("\n\n#endregion");

            helper.sb_static.AppendLine(indent + "}");
            helper.sb_obj.AppendLine(indent + "}");
            helper.sb_intf.AppendLine(indent + "}");

            sb_root.AppendLine(helper.sb_static.ToString()); // append interface
            sb_root.AppendLine(helper.sb_intf.ToString()); // append interface
            sb_root.AppendLine(helper.sb_obj.ToString()); // append object implementing interface
        }

        /// <summary>
        /// This method generates code for single stored procedure
        /// </summary>
        void CreateCodeForSp(StoredProcedure sp, string class_name, SpStreamHelper helper /*StringBuilder sb2, StringBuilder sb3 */ ) {

            string proc_name = pars.sp_prefix + sp.Name;
            bool returns_data = false;
            if (pars.use_sp_with_tables)
                returns_data = sp.Name.StartsWith(pars.prefix_sp_with_tables);

            helper.sb_static.AppendLine(indent + indent + "/// <summary> Wrapper for stored procedure " + sp.Name + ". </summary>");
            helper.sb_static.Append(indent + indent + "public static " + (returns_data ? "DataTable" : "int") + " " + proc_name + "(IDataProviderSp provider");

            helper.sb_intf.AppendLine(indent + indent + "/// <summary> Wrapper for stored procedure " + sp.Name + ". </summary>");
            helper.sb_intf.Append(indent + indent + (returns_data ? "DataTable" : "int") + " " + proc_name + "(");

            helper.sb_obj.AppendLine(indent + indent + "/// <summary> Wrapper for stored procedure " + sp.Name + ". </summary>");
            helper.sb_obj.Append(indent + indent + "public " + (returns_data ? "DataTable" : "int") + " " + proc_name + "(");

            var first = true;
            var sbx = new StringBuilder();
            foreach (SpParam sparam in sp.parameters) {
                sparam.cs_name = CreateValidCsName(sparam.Name);
                if (first) {
                    first = false;
                } else {
                    helper.sb_intf.Append(", ");
                    helper.sb_obj.Append(", ");
                }
                helper.sb_static.AppendLine(", ");
                helper.sb_static.Append(indent + indent + indent + (sparam.is_out ? "out " : "") + type_map.GetSpParamType(sparam.type, sparam.is_out) + " " + sparam.cs_name);
                helper.sb_intf.Append((sparam.is_out ? "out " : "") + type_map.GetSpParamType(sparam.type, sparam.is_out) + " " + sparam.cs_name);
                helper.sb_obj.Append((sparam.is_out ? "out " : "") + type_map.GetSpParamType(sparam.type, sparam.is_out) + " " + sparam.cs_name);
                sbx.Append(", " + (sparam.is_out ? "out " : "") + sparam.cs_name);
            }
            helper.sb_static.AppendLine();
            helper.sb_static.AppendLine(indent + indent + ") {");
            helper.sb_intf.AppendLine(");");

            helper.sb_obj.AppendLine(") { ");
            helper.sb_obj.AppendLine(indent + indent + indent + "return " + class_name + "." + sp.Name + "(provider" + sbx.ToString() + ");");
            helper.sb_obj.AppendLine(indent + indent + "}");

            helper.sb_static.AppendLine(indent + indent + indent + "var cmd = provider.CreateCommand();");
            helper.sb_static.AppendLine(indent + indent + indent + "cmd.CommandText = provider.DatabasePrefix + provider.SchemaPrefix + \"." + sp.Name + "\";");
            helper.sb_static.AppendLine(indent + indent + indent + "cmd.CommandType = CommandType.StoredProcedure;");

            foreach (SpParam sparam in sp.parameters) {
                if (sparam.is_out) {
                    string s = "var par_{0} = AddParameterOut(cmd, \"@{0}\", DbType.{1}, {2}, {3}, {4}, {5});";


                    helper.sb_static.AppendLine(indent + indent + indent + string.Format(s,
                        sparam.Name,
                        sparam.type.ToString(),
                        "DBNull.Value",
                        sparam.orig_parameter.Precision,
                        sparam.orig_parameter.Scale,
                        sparam.orig_parameter.Size));
                } else {
                    string s = "AddParameter(cmd, \"@{0}\", DbType.{1}, (CheckNull({2}) ? (object) DBNull.Value : (object) {2}));";
                    helper.sb_static.AppendLine(indent + indent + indent + string.Format(s, sparam.Name, sparam.type.ToString(), sparam.cs_name));
                }
            }

            if (returns_data) {
                string s = Utils.LoadEmbededResourceAsString("sp_returning_data.txt");
                s = s.Replace("#$#SP_NAME#$#", sp.Name);
                helper.sb_static.Append(s);
            } else {
                helper.sb_static.AppendLine(indent + indent + indent + "int sp_result_value = provider.ExecuteNonQuery(cmd);");
                // set output parameters
                foreach (SpParam sparam in sp.parameters) {
                    if (!sparam.is_out)
                        continue;
                    string s = "{1} = ({2}) par_{0}.Value;";
                    helper.sb_static.AppendLine(indent + indent + indent + string.Format(s, sparam.Name, sparam.cs_name, type_map.GetSpParamType(sparam.type, true)));
                }
                helper.sb_static.AppendLine(indent + indent + indent + "return sp_result_value;");
            }

            helper.sb_static.AppendLine(indent + indent + "}");
            helper.sb_static.AppendLine("");

            CreateCodeForSpDataset(sp, class_name, helper /*sb2, sb3*/);
        }

        /// <summary>
        /// This method generates code for single stored procedure
        /// </summary>
        /// <param name="sp"></param>
        void CreateCodeForSpDataset(StoredProcedure sp, string class_name, SpStreamHelper helper /*StringBuilder sb2, StringBuilder sb_classes*/) {

            string proc_name = pars.sp_prefix + sp.Name + "_DataSet";
            bool returns_data = false;
            if (pars.use_sp_with_tables)
                returns_data = sp.Name.StartsWith(pars.prefix_sp_with_tables);

            if (!returns_data)
                return;

            helper.sb_static.AppendLine(indent + indent + "/// <summary> Wrapper for stored procedure " + sp.Name + " returning DataSet. </summary>");
            helper.sb_static.Append(indent + indent + "public static DataSet " + proc_name + "(IDataProviderSp provider");

            helper.sb_intf.AppendLine(indent + indent + "/// <summary> Wrapper for stored procedure " + sp.Name + " returning DataSet. </summary>");
            helper.sb_intf.Append(indent + indent + "DataSet " + proc_name + "(");

            helper.sb_obj.AppendLine(indent + indent + "/// <summary> Wrapper for stored procedure " + sp.Name + " returning DataSet. </summary>");
            helper.sb_obj.Append(indent + indent + "public DataSet " + proc_name + "(");

            var first = true;
            var sbx = new StringBuilder();
            foreach (SpParam sparam in sp.parameters) {
                sparam.cs_name = CreateValidCsName(sparam.Name);
                if (first) {
                    first = false;
                } else {
                    helper.sb_intf.Append(", ");
                    helper.sb_obj.Append(", ");
                }
                helper.sb_static.AppendLine(", ");
                helper.sb_static.Append(indent + indent + indent + (sparam.is_out ? "out " : "") + type_map.GetSpParamType(sparam.type, sparam.is_out) + " " + sparam.cs_name);
                helper.sb_intf.Append((sparam.is_out ? "out " : "") + type_map.GetSpParamType(sparam.type, sparam.is_out) + " " + sparam.cs_name);
                helper.sb_obj.Append((sparam.is_out ? "out " : "") + type_map.GetSpParamType(sparam.type, sparam.is_out) + " " + sparam.cs_name);
                sbx.Append(", " + (sparam.is_out ? "out " : "") + sparam.cs_name);
            }
            helper.sb_static.AppendLine();
            helper.sb_static.AppendLine(indent + indent + ") {");
            helper.sb_intf.AppendLine(");");

            helper.sb_obj.AppendLine(") { ");
            helper.sb_obj.AppendLine(indent + indent + indent + "return " + class_name + "." + proc_name + "(provider" + sbx.ToString() + ");");
            helper.sb_obj.AppendLine(indent + indent + "}");


            helper.sb_static.AppendLine(indent + indent + indent + "var cmd = provider.CreateCommand();");
            helper.sb_static.AppendLine(indent + indent + indent + "cmd.CommandText = provider.DatabasePrefix + provider.SchemaPrefix + \"." + sp.Name + "\";");
            helper.sb_static.AppendLine(indent + indent + indent + "cmd.CommandType = CommandType.StoredProcedure;");
            foreach (SpParam sparam in sp.parameters) {
                if (sparam.is_out)
                    continue;
                string s = "AddParameter(cmd, \"@{0}\", DbType.{1}, (CheckNull({2}) ? (object) DBNull.Value : (object) {2}));";
                helper.sb_static.AppendLine(indent + indent + indent + string.Format(s, sparam.Name, sparam.type.ToString(), sparam.cs_name));
            }

            helper.sb_static.AppendLine(indent + indent + indent + "return provider.GetDataSet(cmd);");

            helper.sb_static.AppendLine(indent + indent + "}");
            helper.sb_static.AppendLine("");

            CreateCodeForSpWrapper(sp, class_name, helper /* sb2, sb_classes*/);
        }

        /// <summary> This method creates data-wrapper for the dataset that is returned by the stored procedure. </summary>
        /// <param name="sp"> Stored procedure metadata </param>
        void CreateCodeForSpWrapper(StoredProcedure sp, string class_name_sp, SpStreamHelper helper /* StringBuilder sb_intf, StringBuilder sb_classes*/) {
            if (sp.ReturnedData == null)
                return;

            for (var i = 0; i < sp.ReturnedData.Tables.Count; i++) {
                var tab = sp.ReturnedData.Tables[i];
                var sb_columns = new StringBuilder();

                string class_name_sub = pars.class_prefix + Utils.PascalCasing(sp.Name) +
                    "_" + i;
                string base_class = " : " + (pars.base_class_for_tables != null && pars.base_class_for_tables != "" ? pars.base_class_for_tables : "object");
                string db_object_type = "table";

                helper.sb_tabs.AppendLine(indent + "/// <summary> Wrapper for " + db_object_type + " " + sp.Name + ". </summary>");
                helper.sb_tabs.AppendLine(indent + "public partial class " + class_name_sub + base_class + " {");

                foreach (DataColumn field in tab.Columns) {
                    var field_name = (pars.make_names_lowercase ? field.ColumnName.ToLower() : field.ColumnName);

                    helper.sb_tabs.AppendLine();
                    helper.sb_tabs.AppendLine(indent + indent + "/// <summary> The value of the corresponding field in the result of " + sp.Name + " " + db_object_type + ". </summary>");
                    helper.sb_tabs.Append(indent + indent + "[" + Consts.FieldAttr + "(\"" + field_name + "\"");
                    helper.sb_tabs.Append(", ReadOnly=true");
                    helper.sb_tabs.AppendLine(")]");

                    string member_name = CreateValidCsName(pars.member_prefix + field_name);
                    string member_type = type_map.GetDescCs(field.DataType);
                    helper.sb_tabs.Append(indent + indent + "public " + member_type + " " + member_name);
                    helper.sb_tabs.AppendLine(" { get; set; }");

                    sb_columns.AppendLine(indent + indent + indent + "///<summary>Name of column inside this table</summary>");
                    sb_columns.AppendFormat(indent + indent + indent + "public const string {0} = @\"{0}\";", member_name);
                    sb_columns.AppendLine();
                }

                helper.sb_tabs.AppendLine("");
                helper.sb_tabs.AppendLine(indent + indent + "#region Columns Struct");
                helper.sb_tabs.AppendLine(indent + indent + "///<summary>Columns for this table</summary>");
                helper.sb_tabs.AppendLine(indent + indent + "public struct Columns {");
                helper.sb_tabs.Append(sb_columns.ToString());
                helper.sb_tabs.AppendLine(indent + indent + "}");
                helper.sb_tabs.AppendLine(indent + indent + "#endregion");

                helper.sb_tabs.AppendLine(indent + "}");
            }

            string class_name = pars.class_prefix + Utils.PascalCasing(sp.Name);
            //if (!single_table) {

            helper.sb_tabs.AppendLine(indent + "/// <summary> Wrapper for " + sp.Name + ". </summary>");
            helper.sb_tabs.AppendLine(indent + "public partial class " + class_name + " {");
            for (var j = 0; j < sp.ReturnedData.Tables.Count; j++) {
                helper.sb_tabs.AppendLine(indent + indent + "///<summary>Member</summary>");
                var str = "public IList<{0}_{1}> Table_{1};";
                str = string.Format(str, class_name, j);
                helper.sb_tabs.AppendLine(indent + indent + str);
            }
            helper.sb_tabs.AppendLine(indent + "}");

            string proc_name_ds = pars.sp_prefix + sp.Name + "_DataSet";
            string proc_name = pars.sp_prefix + sp.Name + "_DataObject";

            helper.sb_static.AppendLine(indent + indent + "/// <summary> Wrapper for stored procedure " + sp.Name + " returning data object. </summary>");
            helper.sb_static.Append(indent + indent + "public static " + pars.sp_class_name + "." + class_name + " " + proc_name + "(IDataProviderSp provider");

            helper.sb_intf.AppendLine(indent + indent + "/// <summary> Wrapper for stored procedure " + sp.Name + " returning data object. </summary>");
            helper.sb_intf.Append(indent + indent + pars.sp_class_name + "." + class_name + " " + proc_name + "(");

            helper.sb_obj.AppendLine(indent + indent + "/// <summary> Wrapper for stored procedure " + sp.Name + " returning data object. </summary>");
            helper.sb_obj.Append(indent + indent + "public " + pars.sp_class_name + "." + class_name + " " + proc_name + "(");

            var first = true;
            var sbx = new StringBuilder();
            foreach (var sparam in sp.parameters) {
                sparam.cs_name = CreateValidCsName(sparam.Name);
                if (first) {
                    first = false;
                } else {
                    helper.sb_intf.Append(", ");
                    helper.sb_obj.Append(", ");
                }
                helper.sb_static.AppendLine(",");
                helper.sb_static.Append(indent + indent + indent + (sparam.is_out ? "out " : "") + type_map.GetSpParamType(sparam.type, sparam.is_out) + " " + sparam.cs_name);
                helper.sb_intf.Append((sparam.is_out ? "out " : "") + type_map.GetSpParamType(sparam.type, sparam.is_out) + " " + sparam.cs_name);
                helper.sb_obj.Append((sparam.is_out ? "out " : "") + type_map.GetSpParamType(sparam.type, sparam.is_out) + " " + sparam.cs_name);
                sbx.Append(", " + (sparam.is_out ? "out " : "") + sparam.cs_name);
            }
            helper.sb_static.AppendLine();
            helper.sb_static.AppendLine(indent + indent + ") {");

            helper.sb_intf.AppendLine(");");
            helper.sb_obj.AppendLine(") {");
            helper.sb_obj.AppendLine(indent + indent + indent + "return " + class_name_sp + "." + proc_name + "(provider" + sbx.ToString() + ");");
            helper.sb_obj.AppendLine(indent + indent + "}");

            helper.sb_static.Append(indent + indent + indent + "var ds = " + proc_name_ds + "(provider");
            if (sp.parameters.Length > 0) {
                helper.sb_static.Append(", " + string.Join(", ", sp.parameters.Select(x => x.cs_name).ToArray()));
            }
            helper.sb_static.AppendLine(");");

            helper.sb_static.AppendLine(indent + indent + indent + "var res = new " + class_name + "();");

            for (var i = 0; i < sp.ReturnedData.Tables.Count; i++) {
                DataTable tab = sp.ReturnedData.Tables[i];
                var class_name_sub = class_name + "_" + i;
                helper.sb_static.AppendLine(indent + indent + indent + "var tab_" + i + " = new List<" + class_name_sub + ">();");
                helper.sb_static.AppendLine(indent + indent + indent + "foreach (DataRow r" + i + " in ds.Tables[" + i + "].Rows) {");
                helper.sb_static.AppendLine(indent + indent + indent + indent + "var obj = new " + class_name_sub + "();");
                helper.sb_static.AppendLine(indent + indent + indent + indent + "obj.FillObjectFromDataRow(r" + i + ", " + this.pars.sp_data_wrapper_autotrim.ToString().ToLower() + ");");
                helper.sb_static.AppendLine(indent + indent + indent + indent + "tab_" + i + ".Add(obj);");
                helper.sb_static.AppendLine(indent + indent + indent + "}");
                helper.sb_static.AppendLine(indent + indent + indent + "res.Table_" + i + " = tab_" + i + ";");
            }

            helper.sb_static.AppendLine(indent + indent + indent + "return res;");
            helper.sb_static.AppendLine(indent + indent + "}");
            helper.sb_static.AppendLine();
        }

        #endregion
    }
}
