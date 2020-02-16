using System.Collections;
using System.Configuration;
using System.Data.SqlClient;
using System.Data.SqlTypes;
using System.Data;
using System.Linq;
using System.IO;
using System.Reflection;
using System.Text;
using System;

namespace Dal {



    #region data provider interface

    /////////////////////////////////////////////////////////////////////////////////////
    /// <summary> Interface provides basic access to data. </summary>
    ////////////////////////////////////////////////////////////////////////////////////

    public interface IDataProvider {

        /// <summary>this method returns DataTable that is the result of specified SQL command</summary>
        /// <param name="sql">SQL command</param>
        DataTable GetDataTable(string sql);

        /// <summary>this method returns DataSet that is the result of specified SQL command</summary>
        /// <param name="sql">SQL command</param>
        DataSet GetDataSet(string sql);

        /// <summary> This method executes specified command in database </summary>
        /// <param name="sql">Sql command</param>
        void ExecuteNonQuery(string sql);

        /// <summary> This method returns last generated identity </summary>
        long GetIdentity();

        /// <summary> This member is used for injecting linked-server database names... </summary>
        string DatabasePrefix { get; }

        /// <summary> This member is used for injecting schema name (if null, the dbo will be used) </summary>
        string SchemaPrefix { get; }

        /// <summary> This method instructs the provider to skip logging of the next commmand. </summary>
        void SuppressLoggingOfNextCommand();
    }

    /////////////////////////////////////////////////////////////////////////////////////
    /// <summary> Interface provides access to command object. </summary>
    ////////////////////////////////////////////////////////////////////////////////////

    public interface IDataProviderSp : IDataProvider {

        /// <summary> This method gets data as dataset </summary>
        DataSet GetDataSet(IDbCommand cmd);

        /// <summary> This method executes given command </summary>
        int ExecuteNonQuery(IDbCommand cmd);

        /// <summary> This method executes given command and returns data reader for results</summary>
        IDataReader ExecuteReader(IDbCommand cmd);

        /// <summary> This method creates new command object. </summary>
        IDbCommand CreateCommand();
    }

    /////////////////////////////////////////////////////////////////////////////////////
    /// <summary> This interface also requires implementation of IDisposable. </summary>
    ////////////////////////////////////////////////////////////////////////////////////
    public interface IDataProviderSpDisposable : IDataProviderSp, IDisposable {
    }

    #endregion


    #region attributes

    /////////////////////////////////////////////////////////////////////////////////
    /// <summary> This member attribute marks the field as database-bound.
    /// Attribute value ColumnName denotes the database field to bind to.
    /// Attribute value Optional denotes that this field need not be present when
    /// loading data from DataRow.
    /// </summary>
    /////////////////////////////////////////////////////////////////////////////////

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public class DataFieldAttribute : Attribute {

        #region members

        /// <summary> Name of the field to bind the member to. </summary>
        string column_name;

        /// <summary> Flag indicating this field is read-only (system generated/controlled - e.g. timestamp). </summary>
        bool read_only = false;

        /// <summary> Flag indicating this field is (part of) key. </summary>
        bool key = false;

        /// <summary> Flag indicating this field is identity.
        /// It is skipped when constructing insert string. </summary>
        bool identity = false;

        /// <summary> This member denotes maximum length of the string value.
        /// Thi smember is ignored if contained value is not string or if it set to -1. </summary>
        int max_len = -1;

        /// <summary> Flag indicating this field is unicode string - SQL statements should
        /// be generated differently. </summary>
        bool unicode = false;

        /// <summary> Flag indicating this field is nullable. </summary>
        bool nullable = false;

        /// <summary> Type of this field in the database. </summary>
        string db_type = null;

        #endregion

        /// <summary> Public constructor. </summary>
        public DataFieldAttribute(string cn) {
            this.column_name = cn.ToLower();
        }

        /// <summary> Public accessor for the internal member. </summary>
        public string ColumnName {
            get { return column_name; }
            set { column_name = value.ToLower(); }
        }

        /// <summary> Public accessor for the internal member. </summary>
        public bool ReadOnly {
            get { return read_only; }
            set { read_only = value; }
        }

        /// <summary> Public accessor for the internal member. </summary>
        public bool Key {
            get { return key; }
            set { key = value; }
        }

        /// <summary> Public accessor for the internal member. </summary>
        public bool Identity {
            get { return identity; }
            set { identity = value; }
        }

        /// <summary> Public accessor for the internal member. </summary>
        public int MaxLen {
            get { return max_len; }
            set { max_len = (value >= -1 ? value : -1); }
        }

        /// <summary> Public accessor for the internal member. </summary>
        public bool Unicode {
            get { return unicode; }
            set { unicode = value; }
        }

        /// <summary> Public accessor for the internal member. </summary>
        public bool Nullable {
            get { return nullable; }
            set { nullable = value; }
        }

        /// <summary> Public accessor for the internal member. </summary>
        public string DbType {
            get { return db_type; }
            set { db_type = value; }
        }
    }

    ////////////////////////////////////////////////////////////////////////////////
    /// <summary> This member attribute marks the class as database-bound.
    /// Attribute value TableName denotes the database table to bind to.
    /// </summary>
    ////////////////////////////////////////////////////////////////////////////////

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
    public class DataTableAttribute : Attribute {

        /// <summary> Name of the table to bind the object to. </summary>
        string tableName;

        /// <summary> Public constructor that initializes member. </summary>
        public DataTableAttribute(string tableName) { this.tableName = tableName; }

        /// <summary> Public accessor for the internal member. </summary>
        public string TableName {
            get { return tableName; }
            set { tableName = value; }
        }
    }

    ////////////////////////////////////////////////////////////////////////////////
    /// <summary> This member attribute controls if the DAL operation for this class 
    /// are logged or not. </summary>
    ////////////////////////////////////////////////////////////////////////////////

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
    public class DataTableLoggingAttribute : Attribute {

        /// <summary> Skipp logging of insert statements, because they might be really big. </summary>
        public bool SkipLoggingForInsert { get; set; }

        /// <summary> Skipp logging of update statements, because they might be really big. </summary>
        public bool SkipLoggingForUpdate { get; set; }

        /// <summary> Skipp logging of delete statements, because they might be really big. </summary>
        public bool SkipLoggingForDelete { get; set; }
    }

    #endregion

    #region mappings


    /////////////////////////////////////////////////////////////////////////////////////
    /// <summary> This static class contains utility mappings between table 
    /// names and DAL classes. </summary>
    ////////////////////////////////////////////////////////////////////////////////////

    public static class DalMappings {

        /// <summary> Internal structure for data </summary>
        class TableRecord {
            public string TableName { get; set; }
            public Type DalType { get; set; }
            public bool KeyIsIdentity { get; set; }
            public bool KeyIsSingleField { get; set; }
            public string KeyField { get; set; }
            public Type KeyType { get; set; }
        }

        /// <summary> Static mapping from DAL type </summary>
        static Dictionary<string, TableRecord> mapping_name = null;

        /// <summary> Static mapping from table name </summary>
        static Dictionary<Type, TableRecord> mapping_type = null;

        /// <summary> Static constructor - initializes all data </summary>
        static DalMappings() {
            mapping_name = new Dictionary<string, TableRecord>();
            mapping_type = new Dictionary<Type, TableRecord>();

            var types = Assembly.GetExecutingAssembly().GetTypes().ToArray();
            foreach (var t in types) {
                var f = (DataTableAttribute[])t.GetCustomAttributes(typeof(DataTableAttribute), false);
                if (f.Length != 1)
                    continue;
                var name = f[0].TableName;
                var rec = new TableRecord() { DalType = t, TableName = name, KeyIsSingleField = true };
                mapping_name.Add(name, rec);
                mapping_type.Add(t, rec);

                foreach (var property in t.GetMembers()) {

                    var fields = (DataFieldAttribute[])property.GetCustomAttributes(typeof(DataFieldAttribute), true);
                    if (fields.Length == 0) continue;

                    var attr_data = fields[0];

                    if (attr_data.Key) {
                        if (rec.KeyField != null) {
                            rec.KeyIsSingleField = false;
                            break;
                        }
                        rec.KeyField = fields[0].ColumnName;
                        if (attr_data.Identity)
                            rec.KeyIsIdentity = true;
                    }
                }
            }
        }

        /// <summary> This method mapps table name to DAL type </summary>
        public static Type GetTypeForTable(string table_name) {
            if (mapping_name.ContainsKey(table_name))
                return mapping_name[table_name].DalType;
            return null;
        }

        /// <summary> This method mapps DAL type to table name </summary>
        public static string GetTableNameForType(Type t) {
            if (mapping_type.ContainsKey(t))
                return mapping_type[t].TableName;
            return null;
        }

        /// <summary> This method mapps table name to id field </summary>
        public static string GetIdFieldForTable(string table_name) {
            if (mapping_name.ContainsKey(table_name)) {
                var rec = mapping_name[table_name];
                if (rec.KeyIsSingleField)
                    return rec.KeyField;
            }
            return null;
        }
    }

    #endregion

	#region formatting strategy

    ////////////////////////////////////////////////////////////////
    /// <summary>
    /// This interface provides method for different implementations
    /// of value formatting. Uses Strategy design pattern.
    /// </summary>
    ////////////////////////////////////////////////////////////////
    public interface IFormattingStrategy {

        /// <summary> Strategy step </summary>
        /// <param name="val"> Value to be formatted </param>
        /// <returns> Formatted value </returns>
        string FormatValue(object val);
    }

    ////////////////////////////////////////////////////////////////
    /// <summary>
    /// This interface provides method for different implementations
    /// of value formatting. Uses Strategy design pattern.
    /// Provides additional parameter for limiting the
    /// length of string.
    /// </summary>
    ////////////////////////////////////////////////////////////////
    public interface IFormattingStrategyAdvanced : IFormattingStrategy {

        /// <summary> Strategy step - this method provides means to
        /// control number of decimal places that are used for output. </summary>
        /// <param name="val"> Value to be formatted </param>
        /// <param name="dec_places"> number of decimal places to be used </param>
        /// <returns> Formatted value </returns>
        string FormatValueDecPlaces(object val, int? dec_places);

        /// <summary> Strategy step - this method provides means to
        /// cut strings that are too long. </summary>
        /// <param name="val"> Value to be formatted </param>
        /// <param name="max_len"> Maximum length of string </param>
        /// <returns> Formatted value </returns>
        string FormatValue(object val, int max_len);

        /// <summary> Strategy step - this method provides means to
        /// cut strings that are too long. </summary>
        /// <param name="val"> Value to be formatted </param>
        /// <param name="max_len"> Maximum length of string </param>
        /// <param name="is_unicode">Flag if string should be handled as unicode</param>
        /// <returns> Formatted value </returns>
        string FormatValue(object val, int max_len, bool is_unicode);

        /// <summary> Strategy step - it prepares string to be used
        /// for like operations </summary>
        /// <param name="val"> Value to be formatted </param>
        /// <param name="after">is postfix allowed</param>
        /// <param name="before">is prefix allowed </param>
        /// <returns> Formatted value </returns>
        string FormatValueForLike(object val, bool before, bool after);

    }

    ////////////////////////////////////////////////////////////////
    /// <summary>
    /// This class provides formatting of values for T-SQL statements.
    /// </summary>
    /// </remarks>
    ////////////////////////////////////////////////////////////////

    public class FormattingStrategyTSql : IFormattingStrategyAdvanced {

        /// <summary> Constructor </summary>
        public FormattingStrategyTSql() { }

        /// <summary> Implementation of IFormattingStrategy </summary>
        /// <param name="val"> Value to be formatted </param>
        /// <returns> Formatted value </returns>

        string IFormattingStrategy.FormatValue(object val) {
            return this.Format(val, -1, null);
        }

        /// <summary> Implementation of IFormattingStrategyAdvanced </summary>
        /// <param name="val"> Value to be formatted </param>
        /// <param name="max_len"> Maximum length - for string values only </param>
        /// <returns> Formatted value </returns>

        string IFormattingStrategyAdvanced.FormatValue(object val, int max_len) {
            return this.Format(val, max_len, null);
        }

        /// <summary> Implementation of IFormattingStrategyAdvanced </summary>
        /// <param name="val"> Value to be formatted </param>
        /// <param name="max_len"> Maximum length - for string values only </param>
        /// <param name="is_unicode"> Flag if strings should eb handled as unicode </param>
        /// <returns> Formatted value </returns>

        string IFormattingStrategyAdvanced.FormatValue(object val, int max_len, bool is_unicode) {
            return this.Format(val, max_len, null, is_unicode);
        }

        /// <summary> Implementation of IFormattingStrategyAdvanced </summary>
        /// <param name="val"> Value to be formatted </param>
        /// <param name="after">is postfix allowed</param>
        /// <param name="before">is prefix allowed </param>
        /// <returns> Formatted value </returns>

        string IFormattingStrategyAdvanced.FormatValueForLike(object val, bool before, bool after) {
            string s = this.Format(val, -1, null);
            if (s == "NULL") return s;
            s = s.Remove(1, 1);
            s = s.Remove(s.Length - 1, 1);
            s = "'" + (before ? "%" : "") + s + (after ? "%" : "") + "'";
            return s;
        }


        /// <summary> Strategy step - this method provides means to
        /// control number of decimal places that are used for output. </summary>
        /// <param name="val"> Value to be formatted </param>
        /// <param name="dec_places"> number of decimal places to be used </param>
        /// <returns> Formatted value </returns>
        string IFormattingStrategyAdvanced.FormatValueDecPlaces(object val, int? dec_places) {
            return this.Format(val, -1, dec_places);
        }

        /// <summary> Implementation of strategy </summary>
        /// <param name="val"> Value to be formatted </param>
        /// <param name="max_len"> Maximum length - for string values only </param>
        /// <param name="dec_places">Number of decimal places to be used.</param>
        /// <returns> Formatted value </returns>

        string Format(object val, int max_len, int? dec_places) {
            return Format(val, max_len, dec_places, false);
        }
        /// <summary> Implementation of strategy </summary>
        /// <param name="val"> Value to be formatted </param>
        /// <param name="max_len"> Maximum length - for string values only </param>
        /// <param name="dec_places"> Number of decimal places to be used. </param>
        /// <param name="is_unicode"> Flag if is unicode </param>
        /// <returns> Formatted value </returns>

        string Format(object val, int max_len, int? dec_places, bool is_unicode) {

            if (val == null) return "NULL";
            if (val is DBNull) return "NULL";

            string s;
            Type t = val.GetType();

            if (t == typeof(SqlInt32)) {
                SqlInt32 i = (SqlInt32)val;
                if (i.IsNull) s = "NULL";
                else s = i.ToString();

            } else if (t == typeof(int?)) {
                int? i = (int?)val;
                if (!i.HasValue) s = "NULL";
                else s = i.ToString();

            } else if (t == typeof(short?)) {
                short? i = (short?)val;
                if (!i.HasValue) s = "NULL";
                else s = i.ToString();

            } else if (t == typeof(long?)) {
                long? i = (long?)val;
                if (!i.HasValue) s = "NULL";
                else s = i.ToString();

            } else if (t == typeof(byte?)) {
                byte? i = (byte?)val;
                if (!i.HasValue) s = "NULL";
                else s = i.ToString();

            } else if (t == typeof(SqlDecimal)) {
                SqlDecimal d = (SqlDecimal)val;
                if (d.IsNull) s = "NULL";
                else s = (this as IFormattingStrategyAdvanced).FormatValueDecPlaces((decimal)d, dec_places);

            } else if (t == typeof(decimal?)) {
                decimal? d = (decimal?)val;
                if (!d.HasValue) s = "NULL";
                else s = (this as IFormattingStrategyAdvanced).FormatValueDecPlaces((decimal)d, dec_places);

            } else if (t == typeof(decimal)) {
                decimal d = (decimal)val;
                if (dec_places != null)
                    d = Math.Round(d, dec_places ?? 0);
                s = d.ToString(ifp);

            } else if (t == typeof(SqlBoolean)) {
                SqlBoolean b = (SqlBoolean)val;
                if (b.IsNull) s = "NULL";
                else s = this.Format((bool)b, max_len, null);

            } else if (t == typeof(bool?)) {
                bool? b = (bool?)val;
                if (!b.HasValue) s = "NULL";
                else s = this.Format((bool)b, max_len, null, is_unicode);

            } else if (t == typeof(bool)) {
                s = (bool)val ? "1" : "0";

            } else if (t == typeof(SqlDateTime)) {
                SqlDateTime d = (SqlDateTime)val;
                if (d.IsNull) s = "NULL";
                else s = "'" + ((DateTime)d).ToString("s") + "'";

            } else if (t == typeof(DateTime?)) {
                DateTime? d = (DateTime?)val;
                if (!d.HasValue) s = "NULL";
                else s = "'" + ((DateTime)d).ToString("s") + "'";

            } else if (t == typeof(DateTime)) {
                DateTime d = (DateTime)val;
                s = "'" + ((DateTime)d).ToString("s") + "'";

            } else if (t == typeof(char)) {
                s = val.ToString();
                s = s.Replace("'", "''");
                s = (is_unicode ? "N'" : "'") + s + "'";

            } else if (t == typeof(string)) {
                s = val.ToString();
                if (max_len > 0) s = s.Substring(0, Math.Min(s.Length, max_len));
                s = s.Replace("'", "''");
                s = (is_unicode ? "N'" : "'") + s + "'";

            } else if (t == typeof(System.Guid)) {
                s = "'" + ((Guid)val).ToString("B") + "'";

            } else if (t == typeof(SqlGuid)) {
                SqlGuid g = (SqlGuid)val;
                if (g.IsNull) s = "NULL";
                else s = "'" + ((Guid)g).ToString("B") + "'";

            } else if (t == typeof(Guid?)) {
                Guid? g = (Guid?)val;
                if (!g.HasValue) s = "NULL";
                else s = "'" + ((Guid)g).ToString("B") + "'";

            } else if (t == typeof(byte[])) {
                var sb = new System.Text.StringBuilder();
                foreach (var b in (byte[])val) {
                    sb.AppendFormat("{0:x}", b);
                }
                s = "0x" + sb.ToString();

            } else {
                s = val.ToString();
            }
            return s;
        }

        #region ifp

        /// <summary>FormatProvider to be used when interacting with database.
        /// It is static and created once at system startup.</summary>
        static IFormatProvider ifp = CreateIFP();

        /// <summary>Private static method that initializes ifp member.</summary>
        static IFormatProvider CreateIFP() {
            System.Globalization.NumberFormatInfo a = new System.Globalization.NumberFormatInfo();
            a.CurrencyDecimalSeparator = ".";
            a.NumberDecimalSeparator = ".";
            a.PercentDecimalSeparator = ".";
            return a;
        }

        #endregion

    }
    #endregion

    #region object handler

    /////////////////////////////////////////////////////////////////////////////////////
    /// <summary> This class creates insert and update statements for objects
    /// that are data-bound - they are decorated with attributes
    /// DataTable and DataField. </summary>
    ////////////////////////////////////////////////////////////////////////////////////

    public static class DataObjectHandler {

        #region Formatting strategy

        static DataObjectHandler() {
            Fsa = new FormattingStrategyTSql();
            Fs = Fsa;
        }

        /// <summary> This property denotes advanced formatting strategy that needs to be used. </summary>
        public static IFormattingStrategyAdvanced Fsa { get; private set; }

        /// <summary> This property denotes formatting strategy that needs to be used. </summary>
        public static IFormattingStrategyAdvanced Fs { get; private set; }

        #endregion

        #region loading from data row and creation of sql

        /// <summary>another version of a method with the same name.</summary>
        /// <param name="obj"> Object holding the values that need to be inserted into database. </param>
        /// <param name="provider">Provider to generate SQL for </param>
        /// <returns>Insert statement.</returns>

        public static string CreateInsertStatement(this object obj, IDataProvider provider) {
            return CreateInsertStatement(obj, null, provider);
        }

        /// <summary>
        /// This method formats specified value for SQL statement (e.g. encloses strings with parentheses,
        /// converts dates to ISO format, converts boolean to 0/1, uses correct formatter
        /// for decimal values)...
        /// </summary>
        /// <param name="obj">Object we're processing</param>
        /// <param name="property">Member whose value is to be formatted.</param>
        /// <param name="field">Additional information of this member</param>
        /// <returns>Formmated value of specified member, suitable for inclusion
        /// into SQL statements</returns>

        static string FormatValue(object obj, PropertyInfo property, DataFieldAttribute field) {
            string s;
            s = Fsa.FormatValue(property.GetValue(obj, null), field.MaxLen, field.Unicode);
            return s;
        }

        /// <summary> This method creates insert statement for given object. </summary>
        /// <param name="obj"> Object holding the values that need to be inserted into database. </param>
        /// <param name="table_name">name of the table for which this insert should be generated.
        /// if null, the name of the table is determined by DataTableAttribute of the object. </param>
        /// <param name="provider">Provider that sql should be generated for</param>
        /// <returns>Insert statement.</returns>

        public static string CreateInsertStatement(this object obj, string table_name, IDataProvider provider) {
            Type type = obj.GetType();

            StringBuilder sb = new StringBuilder();
            StringBuilder sb2 = new StringBuilder();

            if (table_name == null) {
                table_name = GetTableName(obj);
            }
            sb.AppendFormat("insert into {1}{2}.{0} (\n", table_name, provider.DatabasePrefix, provider.SchemaPrefix);
            sb2.Append(") values (\n");

            // scans all the fields and if proper attribute is found, inserts them into output string
            foreach (var property in type.GetProperties()) {
                var fields = (DataFieldAttribute[])property.GetCustomAttributes(typeof(DataFieldAttribute), true);
                if (fields.Length == 0) continue;
                if (fields[0].Identity) continue;
                if (fields[0].ReadOnly) continue;
                string s = FormatValue(obj, property, fields[0]);
                sb.AppendFormat("\t[{0}],\n", fields[0].ColumnName);
                sb2.AppendFormat("\t{0},\n", s);
            }
            sb.Remove(sb.Length - 2, 1); // remove the last comma
            sb2.Remove(sb2.Length - 2, 1); // remove the last comma
            sb2.Append(")");
            return sb.ToString() + sb2.ToString();
        }

        /// <summary> Simple method that given DAL object creates a list of variable declarations
        /// and setters for them that correspond to values/fields from DAL object. </summary>
        public static string CreateDeclarationStatements(BaseTableWrapper obj) {
            return CreateDeclarationStatements(obj, null, true); // return all fields
        }

        /// <summary> Simple method that given DAL object creates a list of variable declarations
        /// and setters for them that correspond to values/fields from DAL object. </summary>
        public static string CreateDeclarationStatements(BaseTableWrapper obj, string[] target_fields, bool ignore_target_fields) {
            var type = obj.GetType();
            var sb = new StringBuilder();
            if (target_fields == null)
                target_fields = new string[0];

            // scans all the fields and if proper attribute is found, inserts them into output string
            foreach (var property in type.GetProperties()) {
                var fields = (DataFieldAttribute[])property.GetCustomAttributes(typeof(DataFieldAttribute), true);
                if (fields.Length == 0) continue;
                //if (fields[0].Identity) continue;
                //if (fields[0].ReadOnly) continue;
                var field = fields[0];
                if (target_fields.Contains(field.ColumnName)) {
                    if (ignore_target_fields)
                        continue;
                } else {
                    if (!ignore_target_fields)
                        continue;
                }

                var s = FormatValue(obj, property, field);
                sb.AppendFormat("declare @{0} {1}; set @{0} = {2}; ", field.ColumnName, field.DbType, s);
            }
            return sb.ToString();
        }

        /// <summary>
        /// This method creates update statement for all fields of the table
        /// </summary>
        /// <param name="obj">object with values</param>
        /// <param name="provider">Provider to generate SQL for </param>
        /// <returns>update statement</returns>
        public static string CreateUpdateStatement(BaseTableWrapper obj, IDataProvider provider) {
            return CreateUpdateStatement(obj, "", true, provider);
        }

        /// <summary>
        /// This method creates single string from collection of equality expressions.
        /// It also concatenates them using provided flags
        /// </summary>
        static string CreateSingleString(List<string> ar, bool use_comma, bool use_tab, bool use_new_line) {
            var sb = new StringBuilder();
            var pos = 0;
            foreach (var s in ar) {
                if (pos++ > 0) {
                    if (use_comma)
                        sb.Append(", ");
                    else
                        sb.Append(" and ");
                    if (use_new_line) sb.Append("\n");
                }
                if (use_tab) sb.Append("\t");
                sb.Append(s);
            }
            return sb.ToString();
        }

        /// <summary> This method creates update statement for given object. </summary>
        /// <param name="obj"> Object holding the values that need to be updated in the database. </param>
        /// <param name="ignore_target_fields">If true then all fields in parameter target_fields
        /// will be ignored, all the rest will be used. If false, only selected
        /// fields will be used for update. </param>
        /// <param name="target_fields">Comma delimited list of fields that should be used/ignore.
        /// Names are case-insensitive, ' ' characters are trimmed.</param>
        /// <param name="provider">Provider to generate SQL for </param>
        /// <returns>Update statement.</returns>

        public static string CreateUpdateStatement(BaseTableWrapper obj, string target_fields, bool ignore_target_fields, IDataProvider provider) {
            return CreateUpdateStatement(obj, target_fields.Split(','), ignore_target_fields, provider);
        }

        /// <summary> This method creates update statement for given object. </summary>
        /// <param name="obj"> Object holding the values that need to be updated in the database. </param>
        /// <param name="ignore_target_fields">If true then all fields in parameter target_fields
        /// will be ignored, all the rest will be used. If false, only selected
        /// fields will be used for update. </param>
        /// <param name="target_fields">Comma delimited list of fields that should be used/ignore.
        /// Names are case-insensitive, ' ' characters are trimmed.</param>
        /// <param name="provider">Provider to generate SQL for </param>
        /// <returns>Update statement.</returns>

        public static string CreateUpdateStatement(BaseTableWrapper obj, string[] target_fields, bool ignore_target_fields, IDataProvider provider) {
            Type type = obj.GetType();

            string table_name = GetTableName(obj);

            for (int i = 0; i < target_fields.Length; i++)
                target_fields[i] = target_fields[i].Trim().ToLower();

            var ar_body = new List<string>();
            var ar_where = new List<string>();

            // scans all the fields and if proper attribute is found, inserts them into output string
            foreach (var property in type.GetProperties()) {
                var fields = (DataFieldAttribute[])property.GetCustomAttributes(typeof(DataFieldAttribute), true);

                if (fields.Length == 0) continue;

                string column_name = fields[0].ColumnName;
                bool is_in_where_clause = fields[0].Key;
                bool should_include = (!fields[0].Identity) && (!fields[0].ReadOnly); // identities must never be updated

                if (Array.IndexOf(target_fields, column_name) >= 0) {
                    if (ignore_target_fields)
                        should_include = false;
                } else {
                    if (!ignore_target_fields)
                        should_include = false;
                }
                if (!is_in_where_clause && !should_include) continue;

                string s = FormatValue(obj, property, fields[0]);
                string ts = String.Format("{0} = {1}", column_name, s);
                if (should_include) {
                    ar_body.Add(ts);
                }
                if (is_in_where_clause) {
                    ar_where.Add(ts);
                }
            }

            if (ar_where.Count == 0)
                throw new Exception("Where condition not set: no key column found.");

            // finally, construct the result
            string res = "update {3}{4}.{0} set\n {1} where {2}";
            res = String.Format(
                res,
                table_name,
                CreateSingleString(ar_body, true, true, true), // sb.ToString(),
                CreateSingleString(ar_where, false, true, true), // sb2.ToString()
                provider.DatabasePrefix,
                provider.SchemaPrefix
            );
            return res;
        }

        /// <summary> This method creates delete statement </summary>
        /// <param name="obj">object to be deleted</param>
        /// <param name="provider">Provider to generate SQL for </param>
        /// <returns>delete string</returns>

        public static string CreateDeleteStatement(BaseTableWrapper obj, IDataProvider provider) {
            Type type = obj.GetType();
            var ar_where = new List<string>();
            string table_name = GetTableName(obj);

            // scans all the fields and if proper attribute is found, inserts them into output string
            foreach (var property in type.GetProperties()) {
                var fields = (DataFieldAttribute[])property.GetCustomAttributes(typeof(DataFieldAttribute), true);

                if (fields.Length == 0) continue;
                if (!fields[0].Key) continue;

                string s = FormatValue(obj, property, fields[0]);
                string ts = String.Format("{0} = {1}", fields[0].ColumnName, s);
                ar_where.Add(ts);
            }

            // final cosmetics and checks
            if (ar_where.Count == 0)
                throw new Exception("Where condition not set: no key members defined");
            //where_condition = where_condition.Remove(where_condition.Length - 2, 1); // remove the last comma

            // finally, construct the result
            string res = "delete from {2}{3}.{0} where {1}";
            res = String.Format(res, table_name, CreateSingleString(ar_where, false, true, true), provider.DatabasePrefix, provider.SchemaPrefix);
            return res;
        }

        /// <summary> This method creates list of columns that comprise key of this object </summary>
        /// <param name="type"> Type of object to be inspected </param>
        /// <returns> List of member names </returns>

        public static PropertyInfo[] GetIdColumns(Type type) {
            var ar = new List<PropertyInfo>();

            // first determine name of the table
            // scans all the fields and if proper attribute is found, inserts them into output array
            foreach (var property in type.GetProperties()) {
                var fields = (DataFieldAttribute[])property.GetCustomAttributes(typeof(DataFieldAttribute), true);

                if (fields.Length == 0) continue;
                if (fields[0].Key)
                    ar.Add(property);
            }
            return ar.ToArray();
        }

        /// <summary> This method returns array of DataField attributes that are defined for specified member </summary>
        /// <param name="property"> Member object </param>
        /// <returns>Array of DataField attributes</returns>
        public static DataFieldAttribute[] GetDataFieldAttribute(MemberInfo property) {
            return (DataFieldAttribute[])property.GetCustomAttributes(typeof(DataFieldAttribute), true);
        }

        /// <summary>
        /// This method determines name of the database table that provider object is wrapper for.
        /// </summary>
        /// <param name="t">Wrapper object type</param>
        /// <returns>Table name - if no tabl eis specified, it throws an exception</returns>
        public static string GetTableName(Type t) {
            var f = (DataTableAttribute[])t.GetCustomAttributes(typeof(DataTableAttribute), true);
            if (f.Length != 1)
                throw new Exception("Class " + t.FullName + " does not define attribute DataTable.", null);
            return f[0].TableName;
        }

        /// <summary>
        /// This method determines name of the database table that provider object is wrapper for.
        /// </summary>
        /// <param name="obj">Wrapper object</param>
        /// <returns>Table name - if no tabl eis specified, it throws an exception</returns>
        public static string GetTableName(object obj) {
            return GetTableName(obj.GetType());
        }
        /// <summary> This method returns DAL type fro specified table name </summary>
        public static Type GetTypeForTable(string table_name) {
            return DalMappings.GetTypeForTable(table_name);
        }

        /// <summary> Wrapper for another method with the same name.
        /// No trimming is performed</summary>
        public static void FillObjectFromDataRow(this object obj, DataRow r) {
            FillObjectFromDataRow(obj, r, false);
        }

        /// <summary> This method fills given object from the datarow object. </summary>
        /// <param name="obj"> Object to be filled. </param>
        /// <param name="r"> DataRow that holds the values to be filled into the object. </param>
        /// <param name="trim_strings"> If true, all string members are trimmed</param>

        public static void FillObjectFromDataRow(this object obj, DataRow r, bool trim_strings) {
            FillObjectFromDataRow(obj, r, trim_strings, false);
        }

        /// <summary> This method fills given object from the datarow object. </summary>
        /// <param name="obj"> Object to be filled. </param>
        /// <param name="r"> DataRow that holds the values to be filled into the object. </param>
        /// <param name="trim_strings"> If true, all string members are trimmed</param>
        /// <param name="skip_identity"> Should identity fields be skipped </param>

        public static void FillObjectFromDataRow(this object obj, DataRow r, bool trim_strings, bool skip_identity) {
            Type type = obj.GetType();

            // scans all the fields and if proper attribute is found, fetch the value from dat row
            foreach (var property in type.GetProperties()) {
                DataFieldAttribute[] fields = (DataFieldAttribute[])property.GetCustomAttributes(typeof(DataFieldAttribute), true);
                if (fields.Length == 0) continue;
                if (fields[0].Identity && skip_identity) continue;
                string col_name = fields[0].ColumnName;

                // identity column can be missing
                if (r.Table.Columns.Contains(col_name) == false) {
                    string s = "Error while filling member {0} - field \"{1}\" does not exist.";
                    throw new Exception(String.Format(s, property.Name, col_name));
                }

                try {
                    if (property.PropertyType == typeof(Char)) {
                        // check length - it has to be 1
                        string s = (string)r[col_name];
                        if (s.Length != 1) throw new Exception("Cannot fill character member with a string of non-1 length.");
                        property.SetValue(obj, s[0], null);

                    } else if (property.PropertyType == typeof(DateTime?)) {
                        // we need to check for null values
                        if (r.IsNull(col_name)) property.SetValue(obj, null, null);
                        else property.SetValue(obj, (DateTime)r[col_name], null);

                    } else if (property.PropertyType == typeof(int?)) {
                        // we need to check for null values
                        if (r.IsNull(col_name)) property.SetValue(obj, null, null);
                        else property.SetValue(obj, (int)r[col_name], null);

                    } else if (property.PropertyType == typeof(byte?)) {
                        // we need to check for null values
                        if (r.IsNull(col_name)) property.SetValue(obj, null, null);
                        else property.SetValue(obj, (byte)r[col_name], null);

                    } else if (property.PropertyType == typeof(decimal?)) {
                        // we need to check for null values
                        if (r.IsNull(col_name)) property.SetValue(obj, null, null);
                        else property.SetValue(obj, (decimal)r[col_name], null);

                    } else if (property.PropertyType == typeof(short?)) {
                        // we need to check for null values
                        if (r.IsNull(col_name)) property.SetValue(obj, null, null);
                        else property.SetValue(obj, (short)r[col_name], null);

                    } else if (property.PropertyType == typeof(long?)) {
                        // we need to check for null values
                        if (r.IsNull(col_name)) property.SetValue(obj, null, null);
                        else property.SetValue(obj, (long)r[col_name], null);

                    } else if (property.PropertyType == typeof(Guid?)) {
                        // we need to check for null values
                        if (r.IsNull(col_name)) property.SetValue(obj, null, null);
                        else property.SetValue(obj, (Guid)r[col_name], null);

                    } else if (property.PropertyType == typeof(bool?)) {
                        // we need to check for null values
                        if (r.IsNull(col_name)) property.SetValue(obj, null, null);
                        else property.SetValue(obj, (bool)r[col_name], null);

                    } else if (property.PropertyType == typeof(double?)) {
                        // we need to check for null values
                        if (r.IsNull(col_name)) property.SetValue(obj, null, null);
                        else property.SetValue(obj, (double)r[col_name], null);

                    } else if (property.PropertyType == typeof(float?)) {
                        // we need to check for null values
                        if (r.IsNull(col_name)) property.SetValue(obj, null, null);
                        else property.SetValue(obj, (float)r[col_name], null);

                    } else if (property.PropertyType == typeof(float)) {
                        // we need to check for null values
                        if (r.IsNull(col_name)) property.SetValue(obj, null, null);
                        else {
                            double val = (double)r[col_name];
                            property.SetValue(obj, (float)val, null);
                        }
                    } else if (property.PropertyType == typeof(SqlBoolean)) {
                        // we need to check for null values
                        if (r.IsNull(col_name)) property.SetValue(obj, SqlBoolean.Null, null);
                        else property.SetValue(obj, new SqlBoolean((bool)r[col_name]), null);

                    } else if (property.PropertyType == typeof(SqlDecimal)) {
                        // we need to check for null values
                        if (r.IsNull(col_name)) property.SetValue(obj, SqlDecimal.Null, null);
                        else property.SetValue(obj, new SqlDecimal((decimal)r[col_name]), null);

                    } else if (property.PropertyType == typeof(decimal)) {
                        // we need to check for negative zeros
                        decimal d = (decimal)r[col_name];
                        property.SetValue(obj, (d == -0.0m ? 0 : d), null);

                    } else if (property.PropertyType == typeof(SqlByte)) {
                        // we need to check for null values
                        if (r.IsNull(col_name)) property.SetValue(obj, SqlByte.Null, null);
                        else property.SetValue(obj, new SqlByte((byte)r[col_name]), null);

                    } else if (property.PropertyType == typeof(SqlDateTime)) {
                        // we need to check for null values
                        if (r.IsNull(col_name)) property.SetValue(obj, SqlDateTime.Null, null);
                        else property.SetValue(obj, new SqlDateTime((DateTime)r[col_name]), null);

                    } else if (property.PropertyType == typeof(SqlInt32)) {
                        // we need to check for null values
                        if (r.IsNull(col_name)) property.SetValue(obj, SqlInt32.Null, null);
                        else property.SetValue(obj, new SqlInt32((int)r[col_name]), null);

                    } else if (property.PropertyType == typeof(byte[])) {
                        if (r.IsNull(col_name)) property.SetValue(obj, null, null);
                        else {
                            // we support automatic conversion from long to byte[]
                            var data = r[col_name];
                            byte[] data2;
                            if (data.GetType() == typeof(long))
                                data2 = System.BitConverter.GetBytes((long)data);
                            else if (data.GetType() == typeof(ulong))
                                data2 = System.BitConverter.GetBytes((ulong)data);
                            else
                                data2 = (byte[])data;
                            property.SetValue(obj, data2, null);
                        }

                    } else if (property.PropertyType == typeof(string)) {
                        // we need to check for null values
                        if (r.IsNull(col_name)) property.SetValue(obj, null, null);
                        else if (trim_strings) property.SetValue(obj, ((string)r[col_name]).Trim(), null);
                        else property.SetValue(obj, (string)r[col_name], null);

                    } else {
                        property.SetValue(obj, r[col_name], null);
                    }
                } catch (Exception ex) {
                    string s = "Error while filling member {0} with value \"{1}\"";
                    throw new Exception(String.Format(s, property.Name, r[col_name]), ex);
                }
            }
        }
        #endregion
    }

    #endregion

    #region base class for table wrapper objects

    /////////////////////////////////////////////////////////////////////////////////////
    /// <summary> Base class for all table wrappers - used by DataObjectHandler. </summary>
    ////////////////////////////////////////////////////////////////////////////////////

    public partial class BaseTableWrapper {

        #region static data

        /// <summary> This class contains  </summary>
        private class WrapperInfo {

            /// <summary> Wrapper type (derived class) </summary>
            public Type wrapper_type;

            /// <summary> List of key columns </summary>
            public PropertyInfo[] id_columns;

            /// <summary> DataField attibutes of key columns </summary>
            public DataFieldAttribute[] id_columns_dfa;
        }

        /// <summary> Lock object - used to prevent multiple initializations. </summary>
        static object lock_object = new object();

        static Dictionary<Type, WrapperInfo> mapping = new Dictionary<Type, WrapperInfo>();

        /// <summary> List of key columns </summary>
        private PropertyInfo[] IdColumns {
            get {
                Type t = this.GetType();
                if (!mapping.ContainsKey(t))
                    InitializeMapping(t);
                lock (lock_object) {
                    return mapping[t].id_columns;
                }
            }
        }

        /// <summary> DataField attibutes of key columns </summary>
        private DataFieldAttribute[] IdColumnsDfa {
            get {
                Type t = this.GetType();
                if (!mapping.ContainsKey(t))
                    InitializeMapping(t);
                lock (lock_object) {
                    return mapping[t].id_columns_dfa;
                }
            }
        }

        /// <summary>
        /// Static constructor.
        /// It initializes static members.
        /// </summary>
        private static void InitializeMapping(Type t) {
            lock (lock_object) {
                if (mapping.ContainsKey(t)) return;

                WrapperInfo wi = new WrapperInfo();
                wi.wrapper_type = t;
                wi.id_columns = DataObjectHandler.GetIdColumns(t);
                wi.id_columns_dfa = new DataFieldAttribute[wi.id_columns.Length];
                for (int i = 0; i < wi.id_columns.Length; i++) {
                    wi.id_columns_dfa[i] = DataObjectHandler.GetDataFieldAttribute(wi.id_columns[i])[0];
                }
                mapping.Add(t, wi);
            }
        }

        #endregion

        #region supress logging of this type

        /// <summary> Logging settings for derived type </summary>
        private DataTableLoggingAttribute logging_attr = null;

        /// <summary> This method retrieves logging settings. </summary>
        protected DataTableLoggingAttribute GetLoggingFlags() {
            if (logging_attr == null) {
                var attrs = this.GetType().GetCustomAttributes(typeof(DataTableLoggingAttribute), false);
                if (attrs != null && attrs.Length > 0) {
                    logging_attr = attrs[0] as DataTableLoggingAttribute;
                } else {
                    logging_attr = new DataTableLoggingAttribute();
                }
            }
            return logging_attr;
        }

        #endregion

        #region events

        /// <summary> Follow-up code that is raised after the insert. </summary>
        public virtual void AfterInsert(IDataProvider provider) { }

        /// <summary> Follow-up code that is raised after the delete. </summary>
        public virtual void AfterDelete(IDataProvider provider) { }

        /// <summary> Follow-up code that is raised after the update. </summary>
        public virtual void AfterUpdate(IDataProvider provider) { }

        #endregion

        #region insert

        /// <summary> This method returns insert SQL statement for this object. </summary>
        /// <returns></returns>
        public string CreateInsertSqlStatement(IDataProvider provider) {
            return DataObjectHandler.CreateInsertStatement(this, provider);
        }

        /// <summary> This method is used to insert data from this object into data table.
        /// If one of the key columns is identity, this value is retrieved. For this to work, we
        /// assume that there is only one identity column, that it is marked as key and
        /// that data provider avoids returning value of identity that could be created by INSERT trigger. </summary>
        /// <param name="provider">Database access provider</param>
        public virtual void Insert(IDataProvider provider) {

            // insert into database
            string sql = CreateInsertSqlStatement(provider);
            if (GetLoggingFlags().SkipLoggingForInsert)
                provider.SuppressLoggingOfNextCommand();
            provider.ExecuteNonQuery(sql);

            // retrieve identity if needed
            for (int i = 0; i < IdColumnsDfa.Length; i++) {
                if (IdColumnsDfa[i].Identity) {
                    IdColumns[i].SetValue(this, provider.GetIdentity(), null);
                }
            }

            this.AfterInsert(provider);
        }

        #endregion

        #region update

        /// <summary> This method is used to update data from this object into data table </summary>
        /// <param name="provider">Database access provider</param>
        public virtual void Update(IDataProvider provider) {
            string sql = DataObjectHandler.CreateUpdateStatement(this, provider);
            if (GetLoggingFlags().SkipLoggingForUpdate)
                provider.SuppressLoggingOfNextCommand();
            provider.ExecuteNonQuery(sql);
            this.AfterUpdate(provider);
        }

        /// <summary>
        /// This method is used to update data from this object into data table
        /// </summary>
        /// <param name="provider">Database access provider</param>
        /// <param name="ignore_target_fields">Should target fields be updated (false) or ignored (true)</param>
        /// <param name="target_fields">list of fields to update (or ignore)</param>
        public virtual void Update(IDataProvider provider, string target_fields, bool ignore_target_fields) {
            string sql = DataObjectHandler.CreateUpdateStatement(this, target_fields, ignore_target_fields, provider);
            if (GetLoggingFlags().SkipLoggingForUpdate)
                provider.SuppressLoggingOfNextCommand();
            provider.ExecuteNonQuery(sql);
            this.AfterUpdate(provider);
        }

        /// <summary>
        /// This method is used to update data from this object into data table
        /// </summary>
        /// <param name="provider">Database access provider</param>
        /// <param name="ignore_target_fields">Should target fields be updated (false) or ignored (true)</param>
        /// <param name="target_fields">list of fields to update (or ignore)</param>
        public virtual void Update(IDataProvider provider, string[] target_fields, bool ignore_target_fields) {
            string sql = DataObjectHandler.CreateUpdateStatement(this, target_fields, ignore_target_fields, provider);
            if (GetLoggingFlags().SkipLoggingForUpdate)
                provider.SuppressLoggingOfNextCommand();
            provider.ExecuteNonQuery(sql);
            this.AfterUpdate(provider);
        }

        /// <summary>
        /// This method is used to update data from this object into data table
        /// </summary>
        /// <param name="provider">Database access provider</param>
        /// <param name="target_fields">list of fields to update (or ignore)</param>
        public virtual void Update(IDataProvider provider, params string[] target_fields) {
            string sql = DataObjectHandler.CreateUpdateStatement(this, target_fields, false, provider);
            if (GetLoggingFlags().SkipLoggingForUpdate)
                provider.SuppressLoggingOfNextCommand();
            provider.ExecuteNonQuery(sql);
            this.AfterUpdate(provider);
        }

        #endregion

        #region delete

        /// <summary> This method is used to delete record that this object contains </summary>
        /// <param name="provider">Database access provider</param>
        public virtual void Delete(IDataProvider provider) {
            string sql = DataObjectHandler.CreateDeleteStatement(this, provider);
            if (GetLoggingFlags().SkipLoggingForDelete)
                provider.SuppressLoggingOfNextCommand();
            provider.ExecuteNonQuery(sql);
            this.AfterDelete(provider);
        }

        #endregion

        #region loading

        /// <summary> this method loads this object with that data from the database, with specified value of key field </summary>
        /// <param name="provider"> Database access provider </param>
        /// <param name="id"> Id of record to be loaded from the database </param>
        public virtual void Load(IDataProvider provider, object id) {
            if (IdColumns.Length != 1)
                throw new ArgumentException("This class contains invalid number of id columns (expecting one): " + this.GetType().FullName);

            Load(provider, IdColumnsDfa[0].ColumnName, id);
        }

        /// <summary> this method loads this object with that data from the database, with specified value of specified field </summary>
        /// <param name="provider"> Database access provider </param>
        /// <param name="column_name"> Column to look up </param>
        /// <param name="id"> Id of record to be loaded from the database </param>
        public virtual void Load(IDataProvider provider, string column_name, object id) {

            string sql = CreateSelectStatement(DataObjectHandler.GetTableName(this), column_name, id, provider);
            DataTable tab = provider.GetDataTable(sql);

            if (tab.Rows.Count == 0) {
                throw new ArgumentException("No data found with specified id: " + id + ", class = " + this.GetType().FullName);
            }

            if (tab.Rows.Count > 1) {
                throw new ArgumentException("Multiple rows found with specified id: " + id + ", class = " + this.GetType().FullName);
            }

            this.Load(tab.Rows[0]);
        }

        /// <summary> This method constructs select SQL statement for this type</summary>
        /// <param name="id">Id value</param>
        /// <param name="provider">Provider to generate SQL for </param>
        /// <returns>Select statement</returns>
        public string CreateSelectStatement(IDataProvider provider, object id) {
            if (IdColumns.Length != 1)
                throw new ArgumentException("This class contains invalid number of id columns (expecting one): " + this.GetType().FullName);

            return CreateSelectStatement(this.GetType(), IdColumnsDfa[0].ColumnName, id, provider);
        }

        /// <summary> This method constructs select SQL statement for specified type</summary>
        /// <param name="type">Type of table wrapper - contains information about table name </param>
        /// <param name="column_name">Name of the id column</param>
        /// <param name="id">Id value</param>
        /// <param name="provider">Provider to generate SQL for </param>
        /// <returns>Select statement</returns>
        public static string CreateSelectStatement(Type type, string column_name, object id, IDataProvider provider) {
            return CreateSelectStatement(DataObjectHandler.GetTableName(type), column_name, id, provider);
        }

        /// <summary> This method constructs select SQL statement for specified table</summary>
        /// <param name="table_name">Name of the database table </param>
        /// <param name="column_name">Name of the id column</param>
        /// <param name="id">Id value</param>
        /// <param name="provider">Provider to generate SQL for </param>
        /// <returns>Select statement</returns>
        public static string CreateSelectStatement(string table_name, string column_name, object id, IDataProvider provider) {
            string sql = "select * from {3}{4}.{0} where {1} = {2}";
            sql = string.Format(sql, table_name, column_name, DataObjectHandler.Fs.FormatValue(id), provider.DatabasePrefix, provider.SchemaPrefix);
            return sql;
        }

        /// <summary> This method fills the object with values from given data row </summary>
        /// <param name="r"> Data row containing the data </param>
        public void Load(DataRow r) {
            this.FillObjectFromDataRow(r, true);
        }

        ///// <summary> This method fills the object with values from given data row </summary>
        ///// <param name="r"> Data row containing the data </param>
        ///// <param name="skip_identity">should identity fields be skipped</param>
        ///// <param name="trim_strings">should string fields be trimmed</param>
        //public virtual void Load(DataRow r, bool trim_strings, bool skip_identity) {
        //    DataObjectHandler.FillObjectFromDataRow(this, r, trim_strings, skip_identity);
        //}

        #endregion
    }

    ////////////////////////////////////////////////////////////////////////////////////
    /// <summary>
    /// This class provides utility methods for working with BaseTableWrapper.
    /// </summary>
    ////////////////////////////////////////////////////////////////////////////////////

    public static class BaseTableWrapperHelper {

        /// <summary> This method loads single row for database and injects its values into 
        /// a new instance of the provided table wrapper</summary>
        /// <typeparam name="T">Table-wrapper type to be emitted.</typeparam>
        /// <param name="db">Data provider that will return data from the database </param>
        /// <param name="id">Record id</param>
        /// <returns>New instance of table wrapper with data from the database</returns>
        public static T LoadSingle<T>(this IDataProvider db, object id) where T : BaseTableWrapper, new() {
            var rec = new T();
            rec.Load(db, id);
            return rec;
        }

        /// <summary> This method loads single row for database and injects its values into 
        /// a new instance of the provided table wrapper</summary>
        /// <typeparam name="T">Table-wrapper type to be emitted.</typeparam>
        /// <param name="db">Data provider that will return data from the database </param>
        /// <param name="field_name">Field by which to search</param>
        /// <param name="id">Field value to match - exactly one record must be returned.</param>
        /// <returns>New instance of table wrapper with data from the database</returns>
        public static T LoadSingle<T>(this IDataProvider db, string field_name, object id) where T : BaseTableWrapper, new() {
            var rec = new T();
            rec.Load(db, field_name, id);
            return rec;
        }
        
        

        /// <summary> This method returns a list of table-wrapper objects by using specified filter</summary>
        /// <typeparam name="T">Table-wrapper type to be emitted.</typeparam>
        /// <param name="db">Data provider that will return data from the database </param>
        /// <param name="field_name">Field by which to search</param>
        /// <param name="val">Field value to match.</param>
        /// <returns>List of objects that were created with data that was returned from the database</returns>
        public static IList<T> LoadList<T>(this IDataProvider db, string field_name, object val) where T : BaseTableWrapper, new() {
            return new Query<T>()
                .IsEqual(field_name, val)
                .GetList(db);
        }

        /// <summary> This method returns a list of table-wrapper objects by getting all record from the database.</summary>
        /// <typeparam name="T">Table-wrapper type to be emitted.</typeparam>
        /// <param name="db">Data provider that will return data from the database </param>
        /// <returns>List of objects that were created with data that was returned from the database</returns>
        public static IList<T> LoadList<T>(this IDataProvider db) where T : BaseTableWrapper, new() {
            return new Query<T>().GetList(db);
        }
    }


    #endregion


	#region basic SQL-server provider

    ////////////////////////////////////////////////////////////////////////////////////
    /// <summary>
    /// This class encapsulates MS SQL database access.
    /// </summary>
    ////////////////////////////////////////////////////////////////////////////////////

    public class DbAccessSql : IDataProviderSpDisposable {

        #region members

        /// <summary> Connection string for establishing database connection</summary>
        protected string con_string;

        /// <summary> Flag indicating that we're inside the transaction</summary>
        protected bool use_same_connection = false;

        /// <summary> Flag indicating that we should commit transaction if it is still pending on dispose</summary>
        protected bool auto_commit = false;

        /// <summary> Flag indicating that we're inside the transaction</summary>
        protected bool in_transaction = false;

        /// <summary> Connection to database</summary>
        protected System.Data.SqlClient.SqlConnection internal_connection;

        /// <summary> Current transaction - if any.</summary>
        protected System.Data.SqlClient.SqlTransaction internal_transaction;

        /// <summary> This member stores name of connection (from extra connections) </summary>
        protected string name;

        /// <summary>Long timeout for sql commands</summary>
        int _timeout_long = 20 * 60;

        /// <summary>Short timeout for sql commands</summary>
        int _timeout_short = 10;

        /// <summary>Flag indicating that a long timeout should be set</summary>
        bool use_long_timeout = false;

        /// <summary>Utility property for accessing username in session</summary>
        string username {
            get {
                return "g_system";
            }
        }

        bool _skip_logging = false;
        public bool SkipLogging {
            get { return _skip_logging; }
            set { _skip_logging = value; }
        }

        #endregion

        #region transaction methods

        /// <summary>
        /// This method starts the transaction - nested transactions are not permitted.
        /// </summary>
        public void BeginTrans() {
            System.Diagnostics.Trace.TraceInformation("Starting transaction...");
            if (in_transaction) throw new Exception("This object is already in transaction. Nested transactions are not supported.");
            in_transaction = true;
            if (!use_same_connection) {
                internal_connection = GetCon();
            }
            internal_transaction = internal_connection.BeginTransaction();
        }

        /// <summary>
        /// This method commits the transaction
        /// </summary>
        public void Commit() {
            System.Diagnostics.Trace.TraceInformation("Commiting transaction...");
            if (!in_transaction) throw new Exception("This object is not in transaction.");
            in_transaction = false;
            internal_transaction.Commit();
            if (!use_same_connection) {
                internal_connection.Close();
                internal_connection = null;
            }
            internal_transaction = null;
        }

        /// <summary>
        /// This method rolls back the transaction.
        /// </summary>
        public void Rollback() {
            System.Diagnostics.Trace.TraceInformation("Rolling back transaction...");
            if (!in_transaction) throw new Exception("This object is not in transaction.");
            in_transaction = false;
            internal_transaction.Rollback();
            if (!use_same_connection) {
                internal_connection.Close();
                internal_connection = null;
            }
            internal_transaction = null;
        }

        #endregion

        #region constructors

        /// <summary> The wrapper constructor. </summary>
        /// <param name="con">Connection to be used - WARNING: connection will be closed when this
        /// object goes out of scope.</param>
        public DbAccessSql(System.Data.SqlClient.SqlConnection con) {
            internal_connection = con;
            use_same_connection = true;
        }

        /// <summary> The simple constructor. </summary>
        /// <param name="con_str">Connection string to be used 
        /// for establishing database connection.</param>
        public DbAccessSql(string con_str) {
            con_string = con_str;
        }

        /// <summary> The simple constructor. </summary>
        /// <param name="con_str">Connection string to be used 
        /// for establishing database connection.</param>
        /// <param name="use_single_connection">Flag if single connection should be used - otherwise a new connection is used for every request outside a transaction</param>
        /// <param name="auto_start_transaction">Should transaction be started automatically - used only if <see cref="use_single_connection"/> is true. </param>
        /// <param name="auto_commit_on_dispose">Should commit be called if transaction is still pending on Dispose</param>
        public DbAccessSql(string con_str, bool use_single_connection, bool auto_start_transaction, bool auto_commit_on_dispose) {
            con_string = con_str;
            if (use_single_connection) {
                internal_connection = GetCon();
                this.use_same_connection = true;
                if (auto_start_transaction) {
                    BeginTrans();
                    this.auto_commit = auto_commit_on_dispose;
                }
            }
        }

        #endregion

        #region utility methods and properties

        /// <summary> Implementation of IDispose interface.
        /// Cleans up and releases internal members. </summary>
        void IDisposable.Dispose() {
            if (in_transaction && internal_transaction != null) {
                if (auto_commit) {
                    Commit();
                } else {
                    Rollback();
                }
            }

            if (internal_connection == null)
                return;

            internal_connection.Close();
            internal_connection.Dispose();
            internal_connection = null;
        }

        /// <summary> This method creates new database connection. </summary>
        /// <returns> New database connection ready to be used. </returns>
        protected System.Data.SqlClient.SqlConnection GetCon() {
            var r = new System.Data.SqlClient.SqlConnection(con_string);
            r.Open();
            return r;
        }

        /// <summary>
        /// Public read-only accessor to connection string property
        /// </summary>
        public string ConnectionString { get { return con_string; } }

        /// <summary> This method logs information about the 
        /// sql command that is to be executed. </summary>
        /// <param name="sql"> Command to be executed. </param>
        /// <param name="parameters">List of parameters</param>

        void Log(string sql, object[] parameters) {
            if (suppress_next_log) {
                suppress_next_log = false;
                suppress_next_log_after = true;
                return;
            }
            if (suppress_next_log_after) {
                suppress_next_log_after = false;
                return;
            }
            var sb = new StringBuilder();
            sb.Append("Executing command on SQL connection " + this.name + ": sql=[");
            sb.Append(sql);
            sb.Append("]");
            if (parameters != null) {
                foreach (object obj in parameters)
                    sb.Append("[" + obj + "], ");
            }
            System.Diagnostics.Trace.TraceInformation(sb.ToString());
        }

        /// <summary> This method logs information about the 
        /// sql command that is to be executed. </summary>
        /// <param name="cmd"> Command to be executed. </param>
        /// <param name="is_after">Flag if this command has already been executed. </param>

        void Log(IDbCommand cmd, bool is_after) {
            if (suppress_next_log) {
                suppress_next_log = false;
                suppress_next_log_after = true;
                return;
            }
            if (suppress_next_log_after) {
                suppress_next_log_after = false;
                return;
            }
            if (cmd == null) return;
            StringBuilder sb = new StringBuilder();
            sb.Append((is_after ? "Finished executing command " : "Executing command ") + " on '" + this.name + "' connection: sql=[");
            sb.Append(cmd.CommandText);
            sb.Append("] values=");
            foreach (IDataParameter par in cmd.Parameters) {
                sb.Append("[" + par.ParameterName + "=" + par.Value + "]");
            }
            System.Diagnostics.Trace.TraceInformation(sb.ToString());
        }
        #endregion

        #region execution methods

        /// <summary> This method returns DataTable that is the 
        /// result of given SQL command </summary>
        /// <param name="sql_cmd">Sql command</param>
        /// <returns>DataTable containing the data that is 
        /// the result of the query.</returns>
        public DataTable GetDataTab(string sql_cmd) {

            Log(sql_cmd, null);

            if (!in_transaction && !use_same_connection) {
                var con = GetCon();
                try {
                    var cmd = CreateCommand(con);
                    return GetDataSetInner(sql_cmd, cmd, null).Tables[0];
                } finally {
                    con.Close();
                }
            } else {
                var cmd = CreateCommand(internal_connection);
                return GetDataSetInner(sql_cmd, cmd, internal_transaction).Tables[0];
            }
        }

        /// <summary> Helper method for getting dataset</summary>
        /// <param name="sql_cmd">SQL command to execute</param>
        /// <param name="cmd">Comamnd to use execute</param>
        /// <param name="trans">Transaction to be used - can be NULL</param>
        /// <returns>Returns resulting dataset</returns>
        private static DataSet GetDataSetInner(string sql_cmd, System.Data.SqlClient.SqlCommand cmd, System.Data.SqlClient.SqlTransaction trans) {
            cmd.CommandText = sql_cmd;
            if (trans != null) {
                cmd.Transaction = trans;
            }
            var da = new System.Data.SqlClient.SqlDataAdapter(cmd);
            var ds = new DataSet();
            da.Fill(ds);
            return ds;
        }

        /// <summary> This method returns DataSet that is the 
        /// result of given SQL command </summary>
        /// <param name="sql_cmd">Sql command</param>
        /// <returns>DataSet containing the data that is 
        /// the result of the query.</returns>
        public DataSet GetDataSet(string sql_cmd) {

            Log(sql_cmd, null);
            DataSet ds = new DataSet();

            if (!in_transaction && !use_same_connection) {
                System.Data.SqlClient.SqlConnection con = GetCon();
                try {
                    var cmd = CreateCommand(con);
                    return GetDataSetInner(sql_cmd, cmd, null);
                } finally { con.Close(); }
            } else {
                var cmd = CreateCommand(internal_connection);
                return GetDataSetInner(sql_cmd, cmd, internal_transaction);
            }
        }

        /// <summary> This method executes given sql command 
        /// that does not return any data </summary>
        /// <param name="sql_cmd">sql command to be executed</param>
        public void ExecuteNonQuery(string sql_cmd) {
            Log(sql_cmd, null);

            if (!in_transaction && !use_same_connection) {
                var con = GetCon();
                try {
                    var cmd = CreateCommand(con);
                    cmd.CommandText = sql_cmd;
                    cmd.ExecuteNonQuery();
                } finally {
                    con.Close();
                }
            } else {
                var cmd = CreateCommand(internal_connection);
                cmd.CommandText = sql_cmd;
                cmd.Transaction = internal_transaction;
                cmd.ExecuteNonQuery();
            }
        }

        /// <summary> This method executes given sql command with parameters
        /// that does not return any data </summary>
        /// <param name="sql_cmd">sql command to be executed</param>
        /// <param name="parameters">list of parameters</param>
        public void ExecuteNonQuery(string sql_cmd, object[] parameters) {
            Log(sql_cmd, parameters);

            if (!in_transaction && !use_same_connection) {
                var con = GetCon();
                try {
                    var cmd = CreateCommand(con);
                    cmd.CommandText = sql_cmd;
                    AddParameters(cmd, parameters);
                    cmd.ExecuteNonQuery();
                } finally {
                    con.Close();
                }
            } else {
                var cmd = CreateCommand(internal_connection);
                cmd.CommandText = sql_cmd;
                cmd.Transaction = internal_transaction;
                AddParameters(cmd, parameters);
                cmd.ExecuteNonQuery();
            }
        }

        /// <summary> This method adds parameters from given parameter list to OleDb command</summary>
        /// <param name="cmd">Sql command</param>
        /// <param name="parameters">list of parameters</param>
        protected void AddParameters(System.Data.SqlClient.SqlCommand cmd, object[] parameters) {
            for (int i = 0; i < parameters.Length; i++) {
                cmd.Parameters.Add(parameters[i]);
            }
        }

        /// <summary> This method returns singular result of the provided query. </summary>
        /// <param name="sql_cmd">sql command to be executed</param>
        /// <returns>Value of the first field of teh first row.</returns>
        public object ExecuteScalar(string sql_cmd) {
            Log(sql_cmd, null);

            if (!in_transaction && !use_same_connection) {
                var con = GetCon();
                try {
                    var cmd = CreateCommand(con);
                    cmd.CommandText = sql_cmd;
                    return cmd.ExecuteScalar();
                } finally {
                    con.Close();
                }
            } else {
                var cmd = CreateCommand(internal_connection);
                cmd.CommandText = sql_cmd;
                cmd.Transaction = internal_transaction;
                return cmd.ExecuteScalar();
            }
        }

        /// <summary> This method fetches the last created identity </summary>
        /// <returns> Integer value (in database it is decimal!!) </returns>
        public long GetIdentity() {
            var id = ExecuteScalar("select isnull(SCOPE_IDENTITY(), @@identity)");
            return decimal.ToInt64((decimal)id);
        }

        /// <summary>
        /// This method sets a flag for long timeout for sql command
        /// </summary>
        public void SetLongTimeout() {
            use_long_timeout = true;
        }

        /// <summary>
        /// Creates new command for provided connection.
        /// It sets long or short command timeout.
        /// </summary>
        /// <returns>New command object</returns>
        System.Data.SqlClient.SqlCommand CreateCommand(System.Data.SqlClient.SqlConnection _con) {
            var res = _con.CreateCommand();
            if (use_long_timeout) {
                res.CommandTimeout = this._timeout_long;
                use_long_timeout = false;
            } else
                res.CommandTimeout = this._timeout_short;
            return res;
        }

        #endregion

        #region IDataProvider implementation

        /// <summary>IDataProvider implementation</summary>
        DataTable IDataProvider.GetDataTable(string sql) {
            return this.GetDataTab(sql);
        }

        /// <summary>IDataProvider implementation</summary>
        void IDataProvider.ExecuteNonQuery(string sql) {
            this.ExecuteNonQuery(sql);
        }

        /// <summary>IDataProvider implementation</summary>
        long IDataProvider.GetIdentity() {
            return this.GetIdentity();
        }

        /// <summary>IDataProvider implementation</summary>
        string IDataProvider.DatabasePrefix {
            get {
                return string.Empty;
            }
        }

        /// <summary>IDataProviderSp implementation</summary>
        IDbCommand IDataProviderSp.CreateCommand() {
            return new System.Data.SqlClient.SqlCommand("", internal_connection, internal_transaction);
        }

        /// <summary>IDataProviderSp implementation</summary>
        void IDataProvider.SuppressLoggingOfNextCommand() {
            suppress_next_log = true;
            suppress_next_log_after = true;
        }

        /// <summary>Flag for skipping log operation upon the next call</summary>
        bool suppress_next_log = false;

        /// <summary>Flag for skipping log operation upon the next call of "log-after"</summary>
        bool suppress_next_log_after = false;


        #endregion

        #region IDataProviderSp Members

        public DataSet GetDataSet(IDbCommand cmd) {
            Log(cmd, false);
            var da = new System.Data.SqlClient.SqlDataAdapter(cmd as System.Data.SqlClient.SqlCommand);
            var ds = new DataSet();
            da.Fill(ds);
            return ds;
        }

        public int ExecuteNonQuery(IDbCommand cmd) {
            Log(cmd, false);
            return cmd.ExecuteNonQuery();
        }

        public IDataReader ExecuteReader(IDbCommand cmd) {
            Log(cmd, false);
            return cmd.ExecuteReader();
        }

        public string SchemaPrefix {
            get { return "dbo"; }
        }

        #endregion
    }
    
    #endregion


    #region query object

    /////////////////////////////////////////////////////////////////////////////////////
    /// <summary> Query object, used for fetching data from database. </summary>
    /// <remarks>
    /// <b>History</b>
    /// <list type="bullet">
    /// <item>27.06.2007 Vik; created </item>
    /// <item>29.06.2007 Vik; added ExecuteDelete method </item>
    /// <item>02.07.2007 Vik; added ExecuteUpdate method </item>
    /// <item>18.07.2007 Vik; added greater-than and lower-than conditions </item>
    /// <item>26.03.2008 Vik; added "like" condition </item>
    /// <item>07.04.2008 Vik; added IsIn enhancements (parameter that is an array) </item>
    /// <item>10.04.2008 Vik; added "WhereManual" condition </item>
    /// <item>13.02.2009 Vik; Bug id 27705 - added support for ExecuteCount, for TOP X. It is now geenric class. </item>
    /// </list>
    /// </remarks>
    ////////////////////////////////////////////////////////////////////////////////////

    public class Query<T> where T : BaseTableWrapper {

        #region private types

        /// <summary>
        /// List of possible criteria types
        /// </summary>
        private enum QueryType {
            Equal,
            NotEqual,
            Between,
            IsNull,
            IsNotNull,
            IsIn,
            Like,
            IsNotIn,
            IsGreaterThan,
            IsGreaterOrEqualThan,
            IsLowerThan,
            IsLowerOrEqualThan,
            ManualCondition
        }

        /// <summary>
        /// This interface is used for creation of SQL fragments
        /// </summary>
        private interface ISqlGenerator {
            string GetSql();
        }

        /// <summary>
        /// This class will hold order-by instructions
        /// </summary>
        private class OrderByElement : ISqlGenerator {

            bool is_asc = true;
            string field_name;

            /// <summary> Simple constructor </summary>
            public OrderByElement(bool is_asc, string fs) {
                this.is_asc = is_asc;
                this.field_name = fs;
            }

            /// <summary>
            /// This method constructs SQL statement
            /// </summary>
            public string GetSql() {
                //string fields = string.Empty;
                //foreach (string field_name in field_names) {
                //    fields += field_name + (is_asc ? " ASC" : " DESC") + ",";
                //}
                //return fields.TrimEnd(',');
                return field_name + (is_asc ? " ASC" : " DESC");
            }
        }

        /// <summary>
        /// This class will hold single query condition
        /// </summary>
        private class QueryElement : ISqlGenerator {

            QueryType query_type;
            List<object> values;
            string field_name;

            /// <summary>
            /// Helper method that creates QueryElement from specified array.
            /// </summary>
            public static QueryElement CreateFromArray(QueryType qt, string fs, IEnumerable vals) {
                List<object> values = new List<object>();
                IEnumerator e = vals.GetEnumerator();
                while (e.MoveNext())
                    values.Add(e.Current);
                QueryElement q = new QueryElement(qt, fs);
                q.values = values;
                return q;
            }

            /// <summary> Simple constructor </summary>
            public QueryElement(QueryType qt, string fs, params object[] vals) {
                this.query_type = qt;
                this.values = new List<object>(vals);
                this.field_name = fs;
            }

            /// <summary>
            /// This method constructs SQL statement
            /// </summary>
            public string GetSql() {
                StringBuilder sb;
                switch (query_type) {
                    case QueryType.Between:
                        return string.Format(" {0} between {1} and {2} ",
                                             field_name,
                                             DataObjectHandler.Fs.FormatValue(values[0]),
                                             DataObjectHandler.Fs.FormatValue(values[1]));
                    case QueryType.ManualCondition:
                        return " " + values[0] + " ";
                    case QueryType.Equal:
                        return string.Format(" {0} = {1} ", field_name, DataObjectHandler.Fs.FormatValue(values[0]));
                    case QueryType.Like:
                        return string.Format(" {0} like {1} ", field_name, DataObjectHandler.Fs.FormatValue(values[0]));
                    case QueryType.NotEqual:
                        return string.Format(" {0} <> {1} ", field_name, DataObjectHandler.Fs.FormatValue(values[0]));

                    case QueryType.IsIn:
                        if (values == null || values.Count == 0)
                            return "1=0"; // always false

                        sb = new StringBuilder(50, int.MaxValue);
                        foreach (object o in values) {
                            sb.Append(DataObjectHandler.Fs.FormatValue(o));
                            sb.Append(", ");
                        }
                        sb.Remove(sb.Length - 2, 2);

                        return string.Format(" {0} in ({1}) ", field_name, sb.ToString());

                    case QueryType.IsNotIn:
                        if (values == null || values.Count == 0)
                            return "1=1"; // always false

                        sb = new StringBuilder(50, int.MaxValue);
                        foreach (object o in values) {
                            sb.Append(DataObjectHandler.Fs.FormatValue(o));
                            sb.Append(", ");
                        }
                        sb.Remove(sb.Length - 2, 2);

                        return string.Format(" not {0} in ({1}) ", field_name, sb.ToString());

                    case QueryType.IsNull:
                        return string.Format(" {0} is null ", field_name);
                    case QueryType.IsNotNull:
                        return string.Format(" {0} is not null ", field_name);

                    case QueryType.IsGreaterOrEqualThan:
                        return string.Format(" {0} >= {1} ", field_name, DataObjectHandler.Fs.FormatValue(values[0]));
                    case QueryType.IsGreaterThan:
                        return string.Format(" {0} > {1} ", field_name, DataObjectHandler.Fs.FormatValue(values[0]));
                    case QueryType.IsLowerOrEqualThan:
                        return string.Format(" {0} <= {1} ", field_name, DataObjectHandler.Fs.FormatValue(values[0]));
                    case QueryType.IsLowerThan:
                        return string.Format(" {0} < {1} ", field_name, DataObjectHandler.Fs.FormatValue(values[0]));

                    default:
                        throw new ArgumentOutOfRangeException("query_type", query_type, "Invalid operation type in DAL query");
                }
            }
        }

        #endregion

        #region members

        /// <summary>Target type that we're quering for</summary>
        protected Type target_type;

        /// <summary> List of query elements </summary>
        protected ArrayList query_elements = new ArrayList();

        /// <summary> List of query elements </summary>
        protected ArrayList update_elements = new ArrayList();

        /// <summary> List of order by elements </summary>
        protected ArrayList order_by_fields = new ArrayList();

        /// <summary> List of fields to be selected </summary>
        protected ArrayList select_fields = new ArrayList();

        /// <summary> Should we execute count(*) </summary>
        protected bool execute_count = false;

        /// <summary> How many top rows should be returned </summary>
        protected int? top_row = null;

        #endregion

        #region constructors

        /// <summary> Public constructor </summary>
        public Query() {
            target_type = typeof(T);
        }

        #endregion

        #region get SQl statements

        /// <summary> Public constructor for creation of select SQl statement </summary>
        public string GetSelectSql(IDataProvider provider) {

            string field_list;
            if (execute_count) {
                field_list = "count(*)";
            } else {
                if (this.select_fields.Count == 0) {
                    field_list = "*";
                } else {
                    string[] field_list_a = (string[])this.select_fields.ToArray(typeof(string));
                    field_list = string.Join(", ", field_list_a);
                }
                if (top_row.HasValue)
                    field_list = "top " + top_row.Value + " " + field_list;
            }

            string sql = "select {0} from {2}dbo.{1} ";
            sql = string.Format(sql, field_list, DataObjectHandler.GetTableName(target_type), provider.DatabasePrefix);

            if (this.query_elements.Count > 0) {
                sql += " where " + Join(" and ", this.query_elements);
            }

            if (!execute_count && order_by_fields.Count > 0) {
                sql += " order by " + Join(", ", this.order_by_fields);
            }
            return sql;
        }

        /// <summary> This method creates DELETE sql statement</summary>
        public string GetDeleteSql(IDataProvider provider) {
            string sql = "delete from {1}dbo.{0} ";
            sql = string.Format(sql, DataObjectHandler.GetTableName(target_type), provider.DatabasePrefix);

            if (this.query_elements.Count > 0) {
                sql += " where " + Join(" and ", this.query_elements);
            }

            return sql;
        }

        /// <summary> This method creates UPDATE sql statement</summary>
        public string GetUpdateSql(IDataProvider provider) {
            string sql1 = "";
            string sql2 = "";

            if (this.update_elements.Count > 0) {
                sql1 = Join(", ", this.update_elements);
            }

            if (this.query_elements.Count > 0) {
                sql2 = " where " + Join(" and ", this.query_elements);
            }

            string sql = "update {3}dbo.{0} set {1} {2}";
            sql = string.Format(sql, DataObjectHandler.GetTableName(target_type), sql1, sql2, provider.DatabasePrefix);
            return sql;
        }

        #endregion

        #region utility methods

        /// <summary> This method concatenates list of ISqlGenerator-generated sqls</summary>
        /// <param name="str">concatenation string</param>
        /// <param name="ar">array of ISqlGenerator objects</param>
        string Join(string str, ArrayList ar) {
            var sc = new List<string>();
            foreach (ISqlGenerator sg in ar) {
                sc.Add(sg.GetSql());
            }
            return string.Join(str, sc.ToArray());
        }

        #endregion

        #region execution methods

        /// <summary> This method executes SQL against specified provider </summary>
        public DataTable GetDataTable(IDataProvider provider) {
            return provider.GetDataTable(GetSelectSql(provider));
        }

        /// <summary> This method executes SQL against specified provider </summary>
        public Array Execute(IDataProvider provider) {

            this.select_fields.Clear(); // always return all fields
            DataTable tab = provider.GetDataTable(GetSelectSql(provider));

            ArrayList ar = new ArrayList();
            foreach (DataRow r in tab.Rows) {
                BaseTableWrapper btw = Activator.CreateInstance(target_type) as BaseTableWrapper;
                btw.Load(r);
                ar.Add(btw);
            }

            return ar.ToArray(target_type);
        }

        /// <summary> This method executes SQL against specified provider </summary>
        public long ExecuteCount(IDataProvider provider) {
            this.execute_count = true;
            DataTable tab = provider.GetDataTable(GetSelectSql(provider));
            return (long)tab.Rows[0][0];
        }

        /// <summary> This method executes DELETE sql against specified provider </summary>
        public void ExecuteDelete(IDataProvider provider) {
            provider.ExecuteNonQuery(GetDeleteSql(provider));
        }


        /// <summary> This method executes UPDATE sql against specified provider </summary>
        public void ExecuteUpdate(IDataProvider provider) {
            provider.ExecuteNonQuery(GetUpdateSql(provider));
        }

        /// <summary> 
        /// This method executes SQL against specified provider 
        /// and returns list of results already cased as specified type.
        /// </summary>
        public IList<T> GetList(IDataProvider provider) {
            var res = this.Execute(provider);
            List<T> ar = new List<T>();
            foreach (object obj in res) {
                ar.Add(obj as T);
            }
            return ar;
        }

        #endregion

        #region query construction methods

        #region select and update fields

        /// <summary> Adds select-field instruction </summary>
        public Query<T> SelectField(params string[] fields) {
            select_fields.AddRange(fields);
            return this;
        }

        /// <summary> Adds update-field instruction </summary>
        public Query<T> UpdateField(string field_name, object val) {
            update_elements.Add(new QueryElement(QueryType.Equal, field_name, val));
            return this;
        }

        /// <summary> Adds select-field instruction </summary>
        public Query<T> SetTop(int top) {
            this.top_row = top;
            return this;
        }

        #endregion

        #region order by

        /// <summary> Adds specified field or order by expresion into list of order by fields - for ascending sort</summary>
        public Query<T> OrderBy(params string[] order_expresion) {
            return OrderByAsc(order_expresion);
        }

        /// <summary> Adds specified field or order by expresion into list of order by fields - for ascending sort</summary>
        public Query<T> OrderByAsc(params string[] order_expresion) {
            foreach (var s in order_expresion)
                order_by_fields.Add(new OrderByElement(true, s));
            return this;
        }

        /// <summary> Adds specified field or order by expresion into list of order by fields - for descending sort</summary>
        public Query<T> OrderByDesc(params string[] order_expresion) {
            foreach (var s in order_expresion)
                order_by_fields.Add(new OrderByElement(false, s));
            return this;
        }

        #endregion

        #region where conditions

        /// <summary> Adds equality condition </summary>
        public Query<T> IsEqual(string field_name, object val) {
            query_elements.Add(new QueryElement(QueryType.Equal, field_name, val));
            return this;
        }

        /// <summary> Adds like condition </summary>
        public Query<T> IsLike(string field_name, object val) {
            query_elements.Add(new QueryElement(QueryType.Like, field_name, val));
            return this;
        }

        /// <summary> Adds inequality condition </summary>
        public Query<T> IsNotEqual(string field_name, object val) {
            query_elements.Add(new QueryElement(QueryType.NotEqual, field_name, val));
            return this;
        }

        /// <summary> Adds between condition </summary>
        public Query<T> IsBetween(string field_name, object val1, object val2) {
            query_elements.Add(new QueryElement(QueryType.Between, field_name, val1, val2));
            return this;
        }

        /// <summary> Adds is null condition </summary>
        public Query<T> IsNull(string field_name) {
            query_elements.Add(new QueryElement(QueryType.IsNull, field_name));
            return this;
        }

        /// <summary> Adds not is null condition </summary>
        public Query<T> IsNotNull(string field_name) {
            query_elements.Add(new QueryElement(QueryType.IsNotNull, field_name));
            return this;
        }


        #region IsIn methods

        /// <summary> Adds "is in" condition, but accepts an array. Its contents will be stored into query, not the array itself. </summary>
        public Query<T> IsInArray(string field_name, IEnumerable vals) {
            query_elements.Add(QueryElement.CreateFromArray(QueryType.IsIn, field_name, vals));
            return this;
        }

        /// <summary> Adds "is in" condition. It accepts an array of parameters. If you need to send in an already existing array, use
        /// the function with generic type. </summary>
        public Query<T> IsIn(string field_name, params object[] vals) {
            query_elements.Add(new QueryElement(QueryType.IsIn, field_name, vals));
            return this;
        }

        /// <summary> Adds "not is in" condition, but accepts an array. Its contents will be stored into query, not the array itself. </summary>
        public Query<T> IsNotInArray(string field_name, IEnumerable vals) {
            query_elements.Add(QueryElement.CreateFromArray(QueryType.IsNotIn, field_name, vals));
            return this;
        }

        /// <summary> Adds "not is in" condition. If you need to send in an already existing array, use
        /// the function with generic type.  </summary>
        public Query<T> IsNotIn(string field_name, params object[] vals) {
            query_elements.Add(new QueryElement(QueryType.IsNotIn, field_name, vals));
            return this;
        }

        #endregion
        /// <summary> Adds "greater than" condition </summary>
        public Query<T> IsGreaterThan(string field_name, object val) {
            query_elements.Add(new QueryElement(QueryType.IsGreaterThan, field_name, val));
            return this;
        }

        /// <summary> Adds "greater or equal than" condition </summary>
        public Query<T> IsGreaterOrEqualThan(string field_name, object val) {
            query_elements.Add(new QueryElement(QueryType.IsGreaterOrEqualThan, field_name, val));
            return this;
        }

        /// <summary> Adds "lower than" condition </summary>
        public Query<T> IsLowerThan(string field_name, object val) {
            query_elements.Add(new QueryElement(QueryType.IsLowerThan, field_name, val));
            return this;
        }

        /// <summary> Adds "lower or equal than" condition </summary>
        public Query<T> IsLowerOrEqualThan(string field_name, object val) {
            query_elements.Add(new QueryElement(QueryType.IsLowerOrEqualThan, field_name, val));
            return this;
        }

        /// <summary> Adds manually hardcoded condition </summary>
        public Query<T> WhereManual(string condition) {
            query_elements.Add(new QueryElement(QueryType.ManualCondition, null, condition));
            return this;
        }

        #endregion

        #endregion
    }

    #endregion
    #region database tables




































    #endregion
    #region database views








    #endregion
    /// <summary> Public static class containing stored-procedure wrappers. </summary>
    public static class  {
		/// <summary>
		/// this method checks is specified data within provided object is null
		/// </summary>
		/// <param name="o">object containing data</param>
		/// <returns>true if object points to null</returns>
		static bool CheckNull(object o){
			if (o is INullable) return (o as INullable).IsNull;
			if (o is string) return (o as string) == null;
			if (o is Array) return (o as Array) == null;
			return o == null;
		}
		
        /// <summary>
		/// this method creates new parameter
		/// </summary>
		static IDbDataParameter AddParameter(IDbCommand cmd, string name, DbType type, object val) {
            IDbDataParameter par = cmd.CreateParameter();
            par.DbType = type;
            par.ParameterName = name;
            par.Value = val;
            cmd.Parameters.Add(par);
            return par;
        }

        /// <summary>
		/// this method creates new output parameter
		/// </summary>
        static IDbDataParameter AddParameterOut(IDbCommand cmd, string name, DbType type, object val, byte? precision, byte? scale, int? size) {
            IDbDataParameter par = cmd.CreateParameter();
            par.DbType = type;
            par.Direction = ParameterDirection.InputOutput;
            par.ParameterName = name;
            par.Value = val;
            if (precision.HasValue) {
                par.Precision = precision.Value;
                par.Scale = scale.Value;
            }
            if (size.HasValue)
                par.Size = size.Value;
            cmd.Parameters.Add(par);
            return par;
        }

#region grp_admingetdeletedstats
        /// <summary> Wrapper for stored procedure grp_admingetdeletedstats. </summary>
        public static DataTable grp_admingetdeletedstats(IDataProviderSp provider, 
            DateTime? from, 
            DateTime? to
        ) {
            var cmd = provider.CreateCommand();
            cmd.CommandText = provider.DatabasePrefix + provider.SchemaPrefix + ".grp_admingetdeletedstats";
            cmd.CommandType = CommandType.StoredProcedure;
            AddParameter(cmd, "@from", DbType.DateTime, (CheckNull(from) ? (object) DBNull.Value : (object) from));
            AddParameter(cmd, "@to", DbType.DateTime, (CheckNull(to) ? (object) DBNull.Value : (object) to));
        }

        /// <summary> Wrapper for stored procedure grp_admingetdeletedstats returning DataSet. </summary>
        public static DataSet grp_admingetdeletedstats_DataSet(IDataProviderSp provider, 
            DateTime? from, 
            DateTime? to
        ) {
            var cmd = provider.CreateCommand();
            cmd.CommandText = provider.DatabasePrefix + provider.SchemaPrefix + ".grp_admingetdeletedstats";
            cmd.CommandType = CommandType.StoredProcedure;
            AddParameter(cmd, "@from", DbType.DateTime, (CheckNull(from) ? (object) DBNull.Value : (object) from));
            AddParameter(cmd, "@to", DbType.DateTime, (CheckNull(to) ? (object) DBNull.Value : (object) to));
            return provider.GetDataSet(cmd);
        }

        /// <summary> Wrapper for stored procedure grp_admingetdeletedstats returning data object. </summary>
        public static .TableGrpAdmingetdeletedstats grp_admingetdeletedstats_DataObject(IDataProviderSp provider,
            DateTime? from,
            DateTime? to
        ) {
            var ds = grp_admingetdeletedstats_DataSet(provider, from, to);
            var res = new TableGrpAdmingetdeletedstats();
            var tab_0 = new List<TableGrpAdmingetdeletedstats_0>();
            foreach (DataRow r0 in ds.Tables[0].Rows) {
                var obj = new TableGrpAdmingetdeletedstats_0();
                obj.FillObjectFromDataRow(r0, true);
                tab_0.Add(obj);
            }
            res.Table_0 = tab_0;
            return res;
        }



#endregion


#region grp_admingetinvoiceitems
        /// <summary> Wrapper for stored procedure grp_admingetinvoiceitems. </summary>
        public static DataTable grp_admingetinvoiceitems(IDataProviderSp provider, 
            int? id
        ) {
            var cmd = provider.CreateCommand();
            cmd.CommandText = provider.DatabasePrefix + provider.SchemaPrefix + ".grp_admingetinvoiceitems";
            cmd.CommandType = CommandType.StoredProcedure;
            AddParameter(cmd, "@id", DbType.Int32, (CheckNull(id) ? (object) DBNull.Value : (object) id));
        }

        /// <summary> Wrapper for stored procedure grp_admingetinvoiceitems returning DataSet. </summary>
        public static DataSet grp_admingetinvoiceitems_DataSet(IDataProviderSp provider, 
            int? id
        ) {
            var cmd = provider.CreateCommand();
            cmd.CommandText = provider.DatabasePrefix + provider.SchemaPrefix + ".grp_admingetinvoiceitems";
            cmd.CommandType = CommandType.StoredProcedure;
            AddParameter(cmd, "@id", DbType.Int32, (CheckNull(id) ? (object) DBNull.Value : (object) id));
            return provider.GetDataSet(cmd);
        }

        /// <summary> Wrapper for stored procedure grp_admingetinvoiceitems returning data object. </summary>
        public static .TableGrpAdmingetinvoiceitems grp_admingetinvoiceitems_DataObject(IDataProviderSp provider,
            int? id
        ) {
            var ds = grp_admingetinvoiceitems_DataSet(provider, id);
            var res = new TableGrpAdmingetinvoiceitems();
            var tab_0 = new List<TableGrpAdmingetinvoiceitems_0>();
            foreach (DataRow r0 in ds.Tables[0].Rows) {
                var obj = new TableGrpAdmingetinvoiceitems_0();
                obj.FillObjectFromDataRow(r0, true);
                tab_0.Add(obj);
            }
            res.Table_0 = tab_0;
            return res;
        }



#endregion


#region grp_admingetlatest
        /// <summary> Wrapper for stored procedure grp_admingetlatest. </summary>
        public static DataTable grp_admingetlatest(IDataProviderSp provider
        ) {
            var cmd = provider.CreateCommand();
            cmd.CommandText = provider.DatabasePrefix + provider.SchemaPrefix + ".grp_admingetlatest";
            cmd.CommandType = CommandType.StoredProcedure;
        }

        /// <summary> Wrapper for stored procedure grp_admingetlatest returning DataSet. </summary>
        public static DataSet grp_admingetlatest_DataSet(IDataProviderSp provider
        ) {
            var cmd = provider.CreateCommand();
            cmd.CommandText = provider.DatabasePrefix + provider.SchemaPrefix + ".grp_admingetlatest";
            cmd.CommandType = CommandType.StoredProcedure;
            return provider.GetDataSet(cmd);
        }

        /// <summary> Wrapper for stored procedure grp_admingetlatest returning data object. </summary>
        public static .TableGrpAdmingetlatest grp_admingetlatest_DataObject(IDataProviderSp provider
        ) {
            var ds = grp_admingetlatest_DataSet(provider);
            var res = new TableGrpAdmingetlatest();
            var tab_0 = new List<TableGrpAdmingetlatest_0>();
            foreach (DataRow r0 in ds.Tables[0].Rows) {
                var obj = new TableGrpAdmingetlatest_0();
                obj.FillObjectFromDataRow(r0, true);
                tab_0.Add(obj);
            }
            res.Table_0 = tab_0;
            return res;
        }



#endregion


#region grp_admingetlatestdeleted
        /// <summary> Wrapper for stored procedure grp_admingetlatestdeleted. </summary>
        public static DataTable grp_admingetlatestdeleted(IDataProviderSp provider
        ) {
            var cmd = provider.CreateCommand();
            cmd.CommandText = provider.DatabasePrefix + provider.SchemaPrefix + ".grp_admingetlatestdeleted";
            cmd.CommandType = CommandType.StoredProcedure;
        }

        /// <summary> Wrapper for stored procedure grp_admingetlatestdeleted returning DataSet. </summary>
        public static DataSet grp_admingetlatestdeleted_DataSet(IDataProviderSp provider
        ) {
            var cmd = provider.CreateCommand();
            cmd.CommandText = provider.DatabasePrefix + provider.SchemaPrefix + ".grp_admingetlatestdeleted";
            cmd.CommandType = CommandType.StoredProcedure;
            return provider.GetDataSet(cmd);
        }

        /// <summary> Wrapper for stored procedure grp_admingetlatestdeleted returning data object. </summary>
        public static .TableGrpAdmingetlatestdeleted grp_admingetlatestdeleted_DataObject(IDataProviderSp provider
        ) {
            var ds = grp_admingetlatestdeleted_DataSet(provider);
            var res = new TableGrpAdmingetlatestdeleted();
            var tab_0 = new List<TableGrpAdmingetlatestdeleted_0>();
            foreach (DataRow r0 in ds.Tables[0].Rows) {
                var obj = new TableGrpAdmingetlatestdeleted_0();
                obj.FillObjectFromDataRow(r0, true);
                tab_0.Add(obj);
            }
            res.Table_0 = tab_0;
            return res;
        }



#endregion


#region grp_admingetlatestdynamics
        /// <summary> Wrapper for stored procedure grp_admingetlatestdynamics. </summary>
        public static DataTable grp_admingetlatestdynamics(IDataProviderSp provider
        ) {
            var cmd = provider.CreateCommand();
            cmd.CommandText = provider.DatabasePrefix + provider.SchemaPrefix + ".grp_admingetlatestdynamics";
            cmd.CommandType = CommandType.StoredProcedure;
        }

        /// <summary> Wrapper for stored procedure grp_admingetlatestdynamics returning DataSet. </summary>
        public static DataSet grp_admingetlatestdynamics_DataSet(IDataProviderSp provider
        ) {
            var cmd = provider.CreateCommand();
            cmd.CommandText = provider.DatabasePrefix + provider.SchemaPrefix + ".grp_admingetlatestdynamics";
            cmd.CommandType = CommandType.StoredProcedure;
            return provider.GetDataSet(cmd);
        }

        /// <summary> Wrapper for stored procedure grp_admingetlatestdynamics returning data object. </summary>
        public static .TableGrpAdmingetlatestdynamics grp_admingetlatestdynamics_DataObject(IDataProviderSp provider
        ) {
            var ds = grp_admingetlatestdynamics_DataSet(provider);
            var res = new TableGrpAdmingetlatestdynamics();
            var tab_0 = new List<TableGrpAdmingetlatestdynamics_0>();
            foreach (DataRow r0 in ds.Tables[0].Rows) {
                var obj = new TableGrpAdmingetlatestdynamics_0();
                obj.FillObjectFromDataRow(r0, true);
                tab_0.Add(obj);
            }
            res.Table_0 = tab_0;
            return res;
        }



#endregion


#region grp_admingetmonthbyday
        /// <summary> Wrapper for stored procedure grp_admingetmonthbyday. </summary>
        public static DataTable grp_admingetmonthbyday(IDataProviderSp provider, 
            DateTime? d1, 
            DateTime? d2
        ) {
            var cmd = provider.CreateCommand();
            cmd.CommandText = provider.DatabasePrefix + provider.SchemaPrefix + ".grp_admingetmonthbyday";
            cmd.CommandType = CommandType.StoredProcedure;
            AddParameter(cmd, "@d1", DbType.DateTime, (CheckNull(d1) ? (object) DBNull.Value : (object) d1));
            AddParameter(cmd, "@d2", DbType.DateTime, (CheckNull(d2) ? (object) DBNull.Value : (object) d2));
        }

        /// <summary> Wrapper for stored procedure grp_admingetmonthbyday returning DataSet. </summary>
        public static DataSet grp_admingetmonthbyday_DataSet(IDataProviderSp provider, 
            DateTime? d1, 
            DateTime? d2
        ) {
            var cmd = provider.CreateCommand();
            cmd.CommandText = provider.DatabasePrefix + provider.SchemaPrefix + ".grp_admingetmonthbyday";
            cmd.CommandType = CommandType.StoredProcedure;
            AddParameter(cmd, "@d1", DbType.DateTime, (CheckNull(d1) ? (object) DBNull.Value : (object) d1));
            AddParameter(cmd, "@d2", DbType.DateTime, (CheckNull(d2) ? (object) DBNull.Value : (object) d2));
            return provider.GetDataSet(cmd);
        }

        /// <summary> Wrapper for stored procedure grp_admingetmonthbyday returning data object. </summary>
        public static .TableGrpAdmingetmonthbyday grp_admingetmonthbyday_DataObject(IDataProviderSp provider,
            DateTime? d1,
            DateTime? d2
        ) {
            var ds = grp_admingetmonthbyday_DataSet(provider, d1, d2);
            var res = new TableGrpAdmingetmonthbyday();
            var tab_0 = new List<TableGrpAdmingetmonthbyday_0>();
            foreach (DataRow r0 in ds.Tables[0].Rows) {
                var obj = new TableGrpAdmingetmonthbyday_0();
                obj.FillObjectFromDataRow(r0, true);
                tab_0.Add(obj);
            }
            res.Table_0 = tab_0;
            return res;
        }



#endregion


#region grp_admingetmonthbyuserroom
        /// <summary> Wrapper for stored procedure grp_admingetmonthbyuserroom. </summary>
        public static DataTable grp_admingetmonthbyuserroom(IDataProviderSp provider, 
            DateTime? d1, 
            DateTime? d2, 
            bool? only_new, 
            string username
        ) {
            var cmd = provider.CreateCommand();
            cmd.CommandText = provider.DatabasePrefix + provider.SchemaPrefix + ".grp_admingetmonthbyuserroom";
            cmd.CommandType = CommandType.StoredProcedure;
            AddParameter(cmd, "@d1", DbType.DateTime, (CheckNull(d1) ? (object) DBNull.Value : (object) d1));
            AddParameter(cmd, "@d2", DbType.DateTime, (CheckNull(d2) ? (object) DBNull.Value : (object) d2));
            AddParameter(cmd, "@only_new", DbType.Boolean, (CheckNull(only_new) ? (object) DBNull.Value : (object) only_new));
            AddParameter(cmd, "@username", DbType.String, (CheckNull(username) ? (object) DBNull.Value : (object) username));
        }

        /// <summary> Wrapper for stored procedure grp_admingetmonthbyuserroom returning DataSet. </summary>
        public static DataSet grp_admingetmonthbyuserroom_DataSet(IDataProviderSp provider, 
            DateTime? d1, 
            DateTime? d2, 
            bool? only_new, 
            string username
        ) {
            var cmd = provider.CreateCommand();
            cmd.CommandText = provider.DatabasePrefix + provider.SchemaPrefix + ".grp_admingetmonthbyuserroom";
            cmd.CommandType = CommandType.StoredProcedure;
            AddParameter(cmd, "@d1", DbType.DateTime, (CheckNull(d1) ? (object) DBNull.Value : (object) d1));
            AddParameter(cmd, "@d2", DbType.DateTime, (CheckNull(d2) ? (object) DBNull.Value : (object) d2));
            AddParameter(cmd, "@only_new", DbType.Boolean, (CheckNull(only_new) ? (object) DBNull.Value : (object) only_new));
            AddParameter(cmd, "@username", DbType.String, (CheckNull(username) ? (object) DBNull.Value : (object) username));
            return provider.GetDataSet(cmd);
        }

        /// <summary> Wrapper for stored procedure grp_admingetmonthbyuserroom returning data object. </summary>
        public static .TableGrpAdmingetmonthbyuserroom grp_admingetmonthbyuserroom_DataObject(IDataProviderSp provider,
            DateTime? d1,
            DateTime? d2,
            bool? only_new,
            string username
        ) {
            var ds = grp_admingetmonthbyuserroom_DataSet(provider, d1, d2, only_new, username);
            var res = new TableGrpAdmingetmonthbyuserroom();
            var tab_0 = new List<TableGrpAdmingetmonthbyuserroom_0>();
            foreach (DataRow r0 in ds.Tables[0].Rows) {
                var obj = new TableGrpAdmingetmonthbyuserroom_0();
                obj.FillObjectFromDataRow(r0, true);
                tab_0.Add(obj);
            }
            res.Table_0 = tab_0;
            return res;
        }



#endregion


#region grp_adminsearch
        /// <summary> Wrapper for stored procedure grp_adminsearch. </summary>
        public static DataTable grp_adminsearch(IDataProviderSp provider, 
            string username, 
            int? r, 
            DateTime? start, 
            DateTime? end, 
            string deleted
        ) {
            var cmd = provider.CreateCommand();
            cmd.CommandText = provider.DatabasePrefix + provider.SchemaPrefix + ".grp_adminsearch";
            cmd.CommandType = CommandType.StoredProcedure;
            AddParameter(cmd, "@username", DbType.String, (CheckNull(username) ? (object) DBNull.Value : (object) username));
            AddParameter(cmd, "@r", DbType.Int32, (CheckNull(r) ? (object) DBNull.Value : (object) r));
            AddParameter(cmd, "@start", DbType.DateTime, (CheckNull(start) ? (object) DBNull.Value : (object) start));
            AddParameter(cmd, "@end", DbType.DateTime, (CheckNull(end) ? (object) DBNull.Value : (object) end));
            AddParameter(cmd, "@deleted", DbType.AnsiStringFixedLength, (CheckNull(deleted) ? (object) DBNull.Value : (object) deleted));
        }

        /// <summary> Wrapper for stored procedure grp_adminsearch returning DataSet. </summary>
        public static DataSet grp_adminsearch_DataSet(IDataProviderSp provider, 
            string username, 
            int? r, 
            DateTime? start, 
            DateTime? end, 
            string deleted
        ) {
            var cmd = provider.CreateCommand();
            cmd.CommandText = provider.DatabasePrefix + provider.SchemaPrefix + ".grp_adminsearch";
            cmd.CommandType = CommandType.StoredProcedure;
            AddParameter(cmd, "@username", DbType.String, (CheckNull(username) ? (object) DBNull.Value : (object) username));
            AddParameter(cmd, "@r", DbType.Int32, (CheckNull(r) ? (object) DBNull.Value : (object) r));
            AddParameter(cmd, "@start", DbType.DateTime, (CheckNull(start) ? (object) DBNull.Value : (object) start));
            AddParameter(cmd, "@end", DbType.DateTime, (CheckNull(end) ? (object) DBNull.Value : (object) end));
            AddParameter(cmd, "@deleted", DbType.AnsiStringFixedLength, (CheckNull(deleted) ? (object) DBNull.Value : (object) deleted));
            return provider.GetDataSet(cmd);
        }

        /// <summary> Wrapper for stored procedure grp_adminsearch returning data object. </summary>
        public static .TableGrpAdminsearch grp_adminsearch_DataObject(IDataProviderSp provider,
            string username,
            int? r,
            DateTime? start,
            DateTime? end,
            string deleted
        ) {
            var ds = grp_adminsearch_DataSet(provider, username, r, start, end, deleted);
            var res = new TableGrpAdminsearch();
            var tab_0 = new List<TableGrpAdminsearch_0>();
            foreach (DataRow r0 in ds.Tables[0].Rows) {
                var obj = new TableGrpAdminsearch_0();
                obj.FillObjectFromDataRow(r0, true);
                tab_0.Add(obj);
            }
            res.Table_0 = tab_0;
            return res;
        }



#endregion


#region grp_adminsearcharh
        /// <summary> Wrapper for stored procedure grp_adminsearcharh. </summary>
        public static DataTable grp_adminsearcharh(IDataProviderSp provider, 
            string username, 
            int? r, 
            DateTime? start, 
            DateTime? end
        ) {
            var cmd = provider.CreateCommand();
            cmd.CommandText = provider.DatabasePrefix + provider.SchemaPrefix + ".grp_adminsearcharh";
            cmd.CommandType = CommandType.StoredProcedure;
            AddParameter(cmd, "@username", DbType.String, (CheckNull(username) ? (object) DBNull.Value : (object) username));
            AddParameter(cmd, "@r", DbType.Int32, (CheckNull(r) ? (object) DBNull.Value : (object) r));
            AddParameter(cmd, "@start", DbType.DateTime, (CheckNull(start) ? (object) DBNull.Value : (object) start));
            AddParameter(cmd, "@end", DbType.DateTime, (CheckNull(end) ? (object) DBNull.Value : (object) end));
        }

        /// <summary> Wrapper for stored procedure grp_adminsearcharh returning DataSet. </summary>
        public static DataSet grp_adminsearcharh_DataSet(IDataProviderSp provider, 
            string username, 
            int? r, 
            DateTime? start, 
            DateTime? end
        ) {
            var cmd = provider.CreateCommand();
            cmd.CommandText = provider.DatabasePrefix + provider.SchemaPrefix + ".grp_adminsearcharh";
            cmd.CommandType = CommandType.StoredProcedure;
            AddParameter(cmd, "@username", DbType.String, (CheckNull(username) ? (object) DBNull.Value : (object) username));
            AddParameter(cmd, "@r", DbType.Int32, (CheckNull(r) ? (object) DBNull.Value : (object) r));
            AddParameter(cmd, "@start", DbType.DateTime, (CheckNull(start) ? (object) DBNull.Value : (object) start));
            AddParameter(cmd, "@end", DbType.DateTime, (CheckNull(end) ? (object) DBNull.Value : (object) end));
            return provider.GetDataSet(cmd);
        }

        /// <summary> Wrapper for stored procedure grp_adminsearcharh returning data object. </summary>
        public static .TableGrpAdminsearcharh grp_adminsearcharh_DataObject(IDataProviderSp provider,
            string username,
            int? r,
            DateTime? start,
            DateTime? end
        ) {
            var ds = grp_adminsearcharh_DataSet(provider, username, r, start, end);
            var res = new TableGrpAdminsearcharh();
            var tab_0 = new List<TableGrpAdminsearcharh_0>();
            foreach (DataRow r0 in ds.Tables[0].Rows) {
                var obj = new TableGrpAdminsearcharh_0();
                obj.FillObjectFromDataRow(r0, true);
                tab_0.Add(obj);
            }
            res.Table_0 = tab_0;
            return res;
        }



#endregion


#region grp_getmultiweek
        /// <summary> Wrapper for stored procedure grp_getmultiweek. </summary>
        public static DataTable grp_getmultiweek(IDataProviderSp provider, 
            int? id_room, 
            int? window
        ) {
            var cmd = provider.CreateCommand();
            cmd.CommandText = provider.DatabasePrefix + provider.SchemaPrefix + ".grp_getmultiweek";
            cmd.CommandType = CommandType.StoredProcedure;
            AddParameter(cmd, "@id_room", DbType.Int32, (CheckNull(id_room) ? (object) DBNull.Value : (object) id_room));
            AddParameter(cmd, "@window", DbType.Int32, (CheckNull(window) ? (object) DBNull.Value : (object) window));
        }

        /// <summary> Wrapper for stored procedure grp_getmultiweek returning DataSet. </summary>
        public static DataSet grp_getmultiweek_DataSet(IDataProviderSp provider, 
            int? id_room, 
            int? window
        ) {
            var cmd = provider.CreateCommand();
            cmd.CommandText = provider.DatabasePrefix + provider.SchemaPrefix + ".grp_getmultiweek";
            cmd.CommandType = CommandType.StoredProcedure;
            AddParameter(cmd, "@id_room", DbType.Int32, (CheckNull(id_room) ? (object) DBNull.Value : (object) id_room));
            AddParameter(cmd, "@window", DbType.Int32, (CheckNull(window) ? (object) DBNull.Value : (object) window));
            return provider.GetDataSet(cmd);
        }

        /// <summary> Wrapper for stored procedure grp_getmultiweek returning data object. </summary>
        public static .TableGrpGetmultiweek grp_getmultiweek_DataObject(IDataProviderSp provider,
            int? id_room,
            int? window
        ) {
            var ds = grp_getmultiweek_DataSet(provider, id_room, window);
            var res = new TableGrpGetmultiweek();
            var tab_0 = new List<TableGrpGetmultiweek_0>();
            foreach (DataRow r0 in ds.Tables[0].Rows) {
                var obj = new TableGrpGetmultiweek_0();
                obj.FillObjectFromDataRow(r0, true);
                tab_0.Add(obj);
            }
            res.Table_0 = tab_0;
            return res;
        }



#endregion


#region grp_getoverlaps
        /// <summary> Wrapper for stored procedure grp_getoverlaps. </summary>
        public static DataTable grp_getoverlaps(IDataProviderSp provider, 
            int? id_room, 
            DateTime? d1, 
            DateTime? d2, 
            string u, 
            int? id
        ) {
            var cmd = provider.CreateCommand();
            cmd.CommandText = provider.DatabasePrefix + provider.SchemaPrefix + ".grp_getoverlaps";
            cmd.CommandType = CommandType.StoredProcedure;
            AddParameter(cmd, "@id_room", DbType.Int32, (CheckNull(id_room) ? (object) DBNull.Value : (object) id_room));
            AddParameter(cmd, "@d1", DbType.DateTime, (CheckNull(d1) ? (object) DBNull.Value : (object) d1));
            AddParameter(cmd, "@d2", DbType.DateTime, (CheckNull(d2) ? (object) DBNull.Value : (object) d2));
            AddParameter(cmd, "@u", DbType.AnsiString, (CheckNull(u) ? (object) DBNull.Value : (object) u));
            AddParameter(cmd, "@id", DbType.Int32, (CheckNull(id) ? (object) DBNull.Value : (object) id));
        }

        /// <summary> Wrapper for stored procedure grp_getoverlaps returning DataSet. </summary>
        public static DataSet grp_getoverlaps_DataSet(IDataProviderSp provider, 
            int? id_room, 
            DateTime? d1, 
            DateTime? d2, 
            string u, 
            int? id
        ) {
            var cmd = provider.CreateCommand();
            cmd.CommandText = provider.DatabasePrefix + provider.SchemaPrefix + ".grp_getoverlaps";
            cmd.CommandType = CommandType.StoredProcedure;
            AddParameter(cmd, "@id_room", DbType.Int32, (CheckNull(id_room) ? (object) DBNull.Value : (object) id_room));
            AddParameter(cmd, "@d1", DbType.DateTime, (CheckNull(d1) ? (object) DBNull.Value : (object) d1));
            AddParameter(cmd, "@d2", DbType.DateTime, (CheckNull(d2) ? (object) DBNull.Value : (object) d2));
            AddParameter(cmd, "@u", DbType.AnsiString, (CheckNull(u) ? (object) DBNull.Value : (object) u));
            AddParameter(cmd, "@id", DbType.Int32, (CheckNull(id) ? (object) DBNull.Value : (object) id));
            return provider.GetDataSet(cmd);
        }

        /// <summary> Wrapper for stored procedure grp_getoverlaps returning data object. </summary>
        public static .TableGrpGetoverlaps grp_getoverlaps_DataObject(IDataProviderSp provider,
            int? id_room,
            DateTime? d1,
            DateTime? d2,
            string u,
            int? id
        ) {
            var ds = grp_getoverlaps_DataSet(provider, id_room, d1, d2, u, id);
            var res = new TableGrpGetoverlaps();
            var tab_0 = new List<TableGrpGetoverlaps_0>();
            foreach (DataRow r0 in ds.Tables[0].Rows) {
                var obj = new TableGrpGetoverlaps_0();
                obj.FillObjectFromDataRow(r0, true);
                tab_0.Add(obj);
            }
            res.Table_0 = tab_0;
            return res;
        }



#endregion


#region grp_getreservationsforuser
        /// <summary> Wrapper for stored procedure grp_getreservationsforuser. </summary>
        public static DataTable grp_getreservationsforuser(IDataProviderSp provider, 
            string u, 
            DateTime? d1, 
            DateTime? d2
        ) {
            var cmd = provider.CreateCommand();
            cmd.CommandText = provider.DatabasePrefix + provider.SchemaPrefix + ".grp_getreservationsforuser";
            cmd.CommandType = CommandType.StoredProcedure;
            AddParameter(cmd, "@u", DbType.AnsiString, (CheckNull(u) ? (object) DBNull.Value : (object) u));
            AddParameter(cmd, "@d1", DbType.DateTime, (CheckNull(d1) ? (object) DBNull.Value : (object) d1));
            AddParameter(cmd, "@d2", DbType.DateTime, (CheckNull(d2) ? (object) DBNull.Value : (object) d2));
        }

        /// <summary> Wrapper for stored procedure grp_getreservationsforuser returning DataSet. </summary>
        public static DataSet grp_getreservationsforuser_DataSet(IDataProviderSp provider, 
            string u, 
            DateTime? d1, 
            DateTime? d2
        ) {
            var cmd = provider.CreateCommand();
            cmd.CommandText = provider.DatabasePrefix + provider.SchemaPrefix + ".grp_getreservationsforuser";
            cmd.CommandType = CommandType.StoredProcedure;
            AddParameter(cmd, "@u", DbType.AnsiString, (CheckNull(u) ? (object) DBNull.Value : (object) u));
            AddParameter(cmd, "@d1", DbType.DateTime, (CheckNull(d1) ? (object) DBNull.Value : (object) d1));
            AddParameter(cmd, "@d2", DbType.DateTime, (CheckNull(d2) ? (object) DBNull.Value : (object) d2));
            return provider.GetDataSet(cmd);
        }

        /// <summary> Wrapper for stored procedure grp_getreservationsforuser returning data object. </summary>
        public static .TableGrpGetreservationsforuser grp_getreservationsforuser_DataObject(IDataProviderSp provider,
            string u,
            DateTime? d1,
            DateTime? d2
        ) {
            var ds = grp_getreservationsforuser_DataSet(provider, u, d1, d2);
            var res = new TableGrpGetreservationsforuser();
            var tab_0 = new List<TableGrpGetreservationsforuser_0>();
            foreach (DataRow r0 in ds.Tables[0].Rows) {
                var obj = new TableGrpGetreservationsforuser_0();
                obj.FillObjectFromDataRow(r0, true);
                tab_0.Add(obj);
            }
            res.Table_0 = tab_0;
            return res;
        }



#endregion


#region grp_getweekview
        /// <summary> Wrapper for stored procedure grp_getweekview. </summary>
        public static DataTable grp_getweekview(IDataProviderSp provider, 
            int? id_room, 
            int? offset
        ) {
            var cmd = provider.CreateCommand();
            cmd.CommandText = provider.DatabasePrefix + provider.SchemaPrefix + ".grp_getweekview";
            cmd.CommandType = CommandType.StoredProcedure;
            AddParameter(cmd, "@id_room", DbType.Int32, (CheckNull(id_room) ? (object) DBNull.Value : (object) id_room));
            AddParameter(cmd, "@offset", DbType.Int32, (CheckNull(offset) ? (object) DBNull.Value : (object) offset));
        }

        /// <summary> Wrapper for stored procedure grp_getweekview returning DataSet. </summary>
        public static DataSet grp_getweekview_DataSet(IDataProviderSp provider, 
            int? id_room, 
            int? offset
        ) {
            var cmd = provider.CreateCommand();
            cmd.CommandText = provider.DatabasePrefix + provider.SchemaPrefix + ".grp_getweekview";
            cmd.CommandType = CommandType.StoredProcedure;
            AddParameter(cmd, "@id_room", DbType.Int32, (CheckNull(id_room) ? (object) DBNull.Value : (object) id_room));
            AddParameter(cmd, "@offset", DbType.Int32, (CheckNull(offset) ? (object) DBNull.Value : (object) offset));
            return provider.GetDataSet(cmd);
        }

        /// <summary> Wrapper for stored procedure grp_getweekview returning data object. </summary>
        public static .TableGrpGetweekview grp_getweekview_DataObject(IDataProviderSp provider,
            int? id_room,
            int? offset
        ) {
            var ds = grp_getweekview_DataSet(provider, id_room, offset);
            var res = new TableGrpGetweekview();
            var tab_0 = new List<TableGrpGetweekview_0>();
            foreach (DataRow r0 in ds.Tables[0].Rows) {
                var obj = new TableGrpGetweekview_0();
                obj.FillObjectFromDataRow(r0, true);
                tab_0.Add(obj);
            }
            res.Table_0 = tab_0;
            return res;
        }



#endregion


#region grp_glbalance
        /// <summary> Wrapper for stored procedure grp_glbalance. </summary>
        public static DataTable grp_glbalance(IDataProviderSp provider, 
            string username, 
            DateTime? start, 
            DateTime? end, 
            string acc_name
        ) {
            var cmd = provider.CreateCommand();
            cmd.CommandText = provider.DatabasePrefix + provider.SchemaPrefix + ".grp_glbalance";
            cmd.CommandType = CommandType.StoredProcedure;
            AddParameter(cmd, "@username", DbType.String, (CheckNull(username) ? (object) DBNull.Value : (object) username));
            AddParameter(cmd, "@start", DbType.DateTime, (CheckNull(start) ? (object) DBNull.Value : (object) start));
            AddParameter(cmd, "@end", DbType.DateTime, (CheckNull(end) ? (object) DBNull.Value : (object) end));
            AddParameter(cmd, "@acc_name", DbType.AnsiString, (CheckNull(acc_name) ? (object) DBNull.Value : (object) acc_name));
        }

        /// <summary> Wrapper for stored procedure grp_glbalance returning DataSet. </summary>
        public static DataSet grp_glbalance_DataSet(IDataProviderSp provider, 
            string username, 
            DateTime? start, 
            DateTime? end, 
            string acc_name
        ) {
            var cmd = provider.CreateCommand();
            cmd.CommandText = provider.DatabasePrefix + provider.SchemaPrefix + ".grp_glbalance";
            cmd.CommandType = CommandType.StoredProcedure;
            AddParameter(cmd, "@username", DbType.String, (CheckNull(username) ? (object) DBNull.Value : (object) username));
            AddParameter(cmd, "@start", DbType.DateTime, (CheckNull(start) ? (object) DBNull.Value : (object) start));
            AddParameter(cmd, "@end", DbType.DateTime, (CheckNull(end) ? (object) DBNull.Value : (object) end));
            AddParameter(cmd, "@acc_name", DbType.AnsiString, (CheckNull(acc_name) ? (object) DBNull.Value : (object) acc_name));
            return provider.GetDataSet(cmd);
        }

        /// <summary> Wrapper for stored procedure grp_glbalance returning data object. </summary>
        public static .TableGrpGlbalance grp_glbalance_DataObject(IDataProviderSp provider,
            string username,
            DateTime? start,
            DateTime? end,
            string acc_name
        ) {
            var ds = grp_glbalance_DataSet(provider, username, start, end, acc_name);
            var res = new TableGrpGlbalance();
            var tab_0 = new List<TableGrpGlbalance_0>();
            foreach (DataRow r0 in ds.Tables[0].Rows) {
                var obj = new TableGrpGlbalance_0();
                obj.FillObjectFromDataRow(r0, true);
                tab_0.Add(obj);
            }
            res.Table_0 = tab_0;
            return res;
        }



#endregion


#region gsp_adminassigntoinvoice
        /// <summary> Wrapper for stored procedure gsp_adminassigntoinvoice. </summary>
        public static int gsp_adminassigntoinvoice(IDataProviderSp provider, 
            DateTime? d1, 
            DateTime? d2, 
            string username, 
            int? id_inv
        ) {
            var cmd = provider.CreateCommand();
            cmd.CommandText = provider.DatabasePrefix + provider.SchemaPrefix + ".gsp_adminassigntoinvoice";
            cmd.CommandType = CommandType.StoredProcedure;
            AddParameter(cmd, "@d1", DbType.DateTime, (CheckNull(d1) ? (object) DBNull.Value : (object) d1));
            AddParameter(cmd, "@d2", DbType.DateTime, (CheckNull(d2) ? (object) DBNull.Value : (object) d2));
            AddParameter(cmd, "@username", DbType.String, (CheckNull(username) ? (object) DBNull.Value : (object) username));
            AddParameter(cmd, "@id_inv", DbType.Int32, (CheckNull(id_inv) ? (object) DBNull.Value : (object) id_inv));
            int sp_result_value = provider.ExecuteNonQuery(cmd);
            return sp_result_value;
        }



#endregion


#region gsp_admincancelpayment
        /// <summary> Wrapper for stored procedure gsp_admincancelpayment. </summary>
        public static int gsp_admincancelpayment(IDataProviderSp provider, 
            int? payment_id
        ) {
            var cmd = provider.CreateCommand();
            cmd.CommandText = provider.DatabasePrefix + provider.SchemaPrefix + ".gsp_admincancelpayment";
            cmd.CommandType = CommandType.StoredProcedure;
            AddParameter(cmd, "@payment_id", DbType.Int32, (CheckNull(payment_id) ? (object) DBNull.Value : (object) payment_id));
            int sp_result_value = provider.ExecuteNonQuery(cmd);
            return sp_result_value;
        }



#endregion


#region gsp_admindeleteinvoice
        /// <summary> Wrapper for stored procedure gsp_admindeleteinvoice. </summary>
        public static int gsp_admindeleteinvoice(IDataProviderSp provider, 
            int? id
        ) {
            var cmd = provider.CreateCommand();
            cmd.CommandText = provider.DatabasePrefix + provider.SchemaPrefix + ".gsp_admindeleteinvoice";
            cmd.CommandType = CommandType.StoredProcedure;
            AddParameter(cmd, "@id", DbType.Int32, (CheckNull(id) ? (object) DBNull.Value : (object) id));
            int sp_result_value = provider.ExecuteNonQuery(cmd);
            return sp_result_value;
        }



#endregion


#region gsp_adminfixprices
        /// <summary> Wrapper for stored procedure gsp_adminfixprices. </summary>
        public static int gsp_adminfixprices(IDataProviderSp provider, 
            DateTime? d
        ) {
            var cmd = provider.CreateCommand();
            cmd.CommandText = provider.DatabasePrefix + provider.SchemaPrefix + ".gsp_adminfixprices";
            cmd.CommandType = CommandType.StoredProcedure;
            AddParameter(cmd, "@d", DbType.DateTime, (CheckNull(d) ? (object) DBNull.Value : (object) d));
            int sp_result_value = provider.ExecuteNonQuery(cmd);
            return sp_result_value;
        }



#endregion


#region gsp_adminfixpricesforinvoice
        /// <summary> Wrapper for stored procedure gsp_adminfixpricesforinvoice. </summary>
        public static int gsp_adminfixpricesforinvoice(IDataProviderSp provider, 
            int? id_inv
        ) {
            var cmd = provider.CreateCommand();
            cmd.CommandText = provider.DatabasePrefix + provider.SchemaPrefix + ".gsp_adminfixpricesforinvoice";
            cmd.CommandType = CommandType.StoredProcedure;
            AddParameter(cmd, "@id_inv", DbType.Int32, (CheckNull(id_inv) ? (object) DBNull.Value : (object) id_inv));
            int sp_result_value = provider.ExecuteNonQuery(cmd);
            return sp_result_value;
        }



#endregion


#region trp_gettablestats
        /// <summary> Wrapper for stored procedure trp_gettablestats. </summary>
        public static int trp_gettablestats(IDataProviderSp provider
        ) {
            var cmd = provider.CreateCommand();
            cmd.CommandText = provider.DatabasePrefix + provider.SchemaPrefix + ".trp_gettablestats";
            cmd.CommandType = CommandType.StoredProcedure;
            int sp_result_value = provider.ExecuteNonQuery(cmd);
            return sp_result_value;
        }



#endregion


#region tsp_db_tables_size
        /// <summary> Wrapper for stored procedure tsp_db_tables_size. </summary>
        public static int tsp_db_tables_size(IDataProviderSp provider, 
            bool? Sort
        ) {
            var cmd = provider.CreateCommand();
            cmd.CommandText = provider.DatabasePrefix + provider.SchemaPrefix + ".tsp_db_tables_size";
            cmd.CommandType = CommandType.StoredProcedure;
            AddParameter(cmd, "@Sort", DbType.Boolean, (CheckNull(Sort) ? (object) DBNull.Value : (object) Sort));
            int sp_result_value = provider.ExecuteNonQuery(cmd);
            return sp_result_value;
        }



#endregion


#region tsp_generate_inserts
        /// <summary> Wrapper for stored procedure tsp_generate_inserts. </summary>
        public static int tsp_generate_inserts(IDataProviderSp provider, 
            string t_name, 
            string o_name, 
            string i_iden, 
            string d_table, 
            string where_stmt, 
            bool? append
        ) {
            var cmd = provider.CreateCommand();
            cmd.CommandText = provider.DatabasePrefix + provider.SchemaPrefix + ".tsp_generate_inserts";
            cmd.CommandType = CommandType.StoredProcedure;
            AddParameter(cmd, "@t_name", DbType.AnsiString, (CheckNull(t_name) ? (object) DBNull.Value : (object) t_name));
            AddParameter(cmd, "@o_name", DbType.AnsiString, (CheckNull(o_name) ? (object) DBNull.Value : (object) o_name));
            AddParameter(cmd, "@i_iden", DbType.AnsiString, (CheckNull(i_iden) ? (object) DBNull.Value : (object) i_iden));
            AddParameter(cmd, "@d_table", DbType.AnsiString, (CheckNull(d_table) ? (object) DBNull.Value : (object) d_table));
            AddParameter(cmd, "@where_stmt", DbType.AnsiString, (CheckNull(where_stmt) ? (object) DBNull.Value : (object) where_stmt));
            AddParameter(cmd, "@append", DbType.Boolean, (CheckNull(append) ? (object) DBNull.Value : (object) append));
            int sp_result_value = provider.ExecuteNonQuery(cmd);
            return sp_result_value;
        }



#endregion


#region tsp_recompileallobjects
        /// <summary> Wrapper for stored procedure tsp_recompileallobjects. </summary>
        public static int tsp_recompileallobjects(IDataProviderSp provider
        ) {
            var cmd = provider.CreateCommand();
            cmd.CommandText = provider.DatabasePrefix + provider.SchemaPrefix + ".tsp_recompileallobjects";
            cmd.CommandType = CommandType.StoredProcedure;
            int sp_result_value = provider.ExecuteNonQuery(cmd);
            return sp_result_value;
        }



#endregion


#region result-table wrappers
    /// <summary> Wrapper for table grp_admingetdeletedstats. </summary>
    public partial class TableGrpAdmingetdeletedstats_0 : object {

        /// <summary> The value of the corresponding field in the result of grp_admingetdeletedstats table. </summary>
        [DataField("username", ReadOnly=true)]
        public string username { get; set; }

        /// <summary> The value of the corresponding field in the result of grp_admingetdeletedstats table. </summary>
        [DataField("y", ReadOnly=true)]
        public int? y { get; set; }

        /// <summary> The value of the corresponding field in the result of grp_admingetdeletedstats table. </summary>
        [DataField("m", ReadOnly=true)]
        public int? m { get; set; }

        /// <summary> The value of the corresponding field in the result of grp_admingetdeletedstats table. </summary>
        [DataField("deleted_hours", ReadOnly=true)]
        public decimal? deleted_hours { get; set; }

        #region Columns Struct
        ///<summary>Columns for this table</summary>
        public struct Columns {
            ///<summary>Name of column inside this table</summary>
            public const string username = @"username";
            ///<summary>Name of column inside this table</summary>
            public const string y = @"y";
            ///<summary>Name of column inside this table</summary>
            public const string m = @"m";
            ///<summary>Name of column inside this table</summary>
            public const string deleted_hours = @"deleted_hours";
        }
        #endregion
    }
    /// <summary> Wrapper for grp_admingetdeletedstats. </summary>
    public partial class TableGrpAdmingetdeletedstats {
        ///<summary>Member</summary>
        public IList<TableGrpAdmingetdeletedstats_0> Table_0;
    }
    /// <summary> Wrapper for table grp_admingetinvoiceitems. </summary>
    public partial class TableGrpAdmingetinvoiceitems_0 : object {

        /// <summary> The value of the corresponding field in the result of grp_admingetinvoiceitems table. </summary>
        [DataField("amt", ReadOnly=true)]
        public decimal? amt { get; set; }

        /// <summary> The value of the corresponding field in the result of grp_admingetinvoiceitems table. </summary>
        [DataField("amt_disc", ReadOnly=true)]
        public decimal? amt_disc { get; set; }

        /// <summary> The value of the corresponding field in the result of grp_admingetinvoiceitems table. </summary>
        [DataField("amt_net", ReadOnly=true)]
        public decimal? amt_net { get; set; }

        /// <summary> The value of the corresponding field in the result of grp_admingetinvoiceitems table. </summary>
        [DataField("amt_orig", ReadOnly=true)]
        public decimal? amt_orig { get; set; }

        /// <summary> The value of the corresponding field in the result of grp_admingetinvoiceitems table. </summary>
        [DataField("amt_tax", ReadOnly=true)]
        public decimal? amt_tax { get; set; }

        /// <summary> The value of the corresponding field in the result of grp_admingetinvoiceitems table. </summary>
        [DataField("description", ReadOnly=true)]
        public string description { get; set; }

        /// <summary> The value of the corresponding field in the result of grp_admingetinvoiceitems table. </summary>
        [DataField("id", ReadOnly=true)]
        public int? id { get; set; }

        /// <summary> The value of the corresponding field in the result of grp_admingetinvoiceitems table. </summary>
        [DataField("id_inv", ReadOnly=true)]
        public int? id_inv { get; set; }

        /// <summary> The value of the corresponding field in the result of grp_admingetinvoiceitems table. </summary>
        [DataField("perc_disc", ReadOnly=true)]
        public decimal? perc_disc { get; set; }

        /// <summary> The value of the corresponding field in the result of grp_admingetinvoiceitems table. </summary>
        [DataField("price", ReadOnly=true)]
        public decimal? price { get; set; }

        #region Columns Struct
        ///<summary>Columns for this table</summary>
        public struct Columns {
            ///<summary>Name of column inside this table</summary>
            public const string amt = @"amt";
            ///<summary>Name of column inside this table</summary>
            public const string amt_disc = @"amt_disc";
            ///<summary>Name of column inside this table</summary>
            public const string amt_net = @"amt_net";
            ///<summary>Name of column inside this table</summary>
            public const string amt_orig = @"amt_orig";
            ///<summary>Name of column inside this table</summary>
            public const string amt_tax = @"amt_tax";
            ///<summary>Name of column inside this table</summary>
            public const string description = @"description";
            ///<summary>Name of column inside this table</summary>
            public const string id = @"id";
            ///<summary>Name of column inside this table</summary>
            public const string id_inv = @"id_inv";
            ///<summary>Name of column inside this table</summary>
            public const string perc_disc = @"perc_disc";
            ///<summary>Name of column inside this table</summary>
            public const string price = @"price";
        }
        #endregion
    }
    /// <summary> Wrapper for grp_admingetinvoiceitems. </summary>
    public partial class TableGrpAdmingetinvoiceitems {
        ///<summary>Member</summary>
        public IList<TableGrpAdmingetinvoiceitems_0> Table_0;
    }
    /// <summary> Wrapper for table grp_admingetlatest. </summary>
    public partial class TableGrpAdmingetlatest_0 : object {

        /// <summary> The value of the corresponding field in the result of grp_admingetlatest table. </summary>
        [DataField("entered", ReadOnly=true)]
        public DateTime? entered { get; set; }

        /// <summary> The value of the corresponding field in the result of grp_admingetlatest table. </summary>
        [DataField("username", ReadOnly=true)]
        public string username { get; set; }

        /// <summary> The value of the corresponding field in the result of grp_admingetlatest table. </summary>
        [DataField("target_date", ReadOnly=true)]
        public DateTime? target_date { get; set; }

        /// <summary> The value of the corresponding field in the result of grp_admingetlatest table. </summary>
        [DataField("len", ReadOnly=true)]
        public double? len { get; set; }

        /// <summary> The value of the corresponding field in the result of grp_admingetlatest table. </summary>
        [DataField("description", ReadOnly=true)]
        public string description { get; set; }

        /// <summary> The value of the corresponding field in the result of grp_admingetlatest table. </summary>
        [DataField("reservation_type", ReadOnly=true)]
        public string reservation_type { get; set; }

        /// <summary> The value of the corresponding field in the result of grp_admingetlatest table. </summary>
        [DataField("is_deleted", ReadOnly=true)]
        public bool? is_deleted { get; set; }

        /// <summary> The value of the corresponding field in the result of grp_admingetlatest table. </summary>
        [DataField("deleted", ReadOnly=true)]
        public DateTime? deleted { get; set; }

        /// <summary> The value of the corresponding field in the result of grp_admingetlatest table. </summary>
        [DataField("id_room", ReadOnly=true)]
        public int? id_room { get; set; }

        /// <summary> The value of the corresponding field in the result of grp_admingetlatest table. </summary>
        [DataField("is_group", ReadOnly=true)]
        public bool? is_group { get; set; }

        /// <summary> The value of the corresponding field in the result of grp_admingetlatest table. </summary>
        [DataField("room_name", ReadOnly=true)]
        public string room_name { get; set; }

        #region Columns Struct
        ///<summary>Columns for this table</summary>
        public struct Columns {
            ///<summary>Name of column inside this table</summary>
            public const string entered = @"entered";
            ///<summary>Name of column inside this table</summary>
            public const string username = @"username";
            ///<summary>Name of column inside this table</summary>
            public const string target_date = @"target_date";
            ///<summary>Name of column inside this table</summary>
            public const string len = @"len";
            ///<summary>Name of column inside this table</summary>
            public const string description = @"description";
            ///<summary>Name of column inside this table</summary>
            public const string reservation_type = @"reservation_type";
            ///<summary>Name of column inside this table</summary>
            public const string is_deleted = @"is_deleted";
            ///<summary>Name of column inside this table</summary>
            public const string deleted = @"deleted";
            ///<summary>Name of column inside this table</summary>
            public const string id_room = @"id_room";
            ///<summary>Name of column inside this table</summary>
            public const string is_group = @"is_group";
            ///<summary>Name of column inside this table</summary>
            public const string room_name = @"room_name";
        }
        #endregion
    }
    /// <summary> Wrapper for grp_admingetlatest. </summary>
    public partial class TableGrpAdmingetlatest {
        ///<summary>Member</summary>
        public IList<TableGrpAdmingetlatest_0> Table_0;
    }
    /// <summary> Wrapper for table grp_admingetlatestdeleted. </summary>
    public partial class TableGrpAdmingetlatestdeleted_0 : object {

        /// <summary> The value of the corresponding field in the result of grp_admingetlatestdeleted table. </summary>
        [DataField("entered", ReadOnly=true)]
        public DateTime? entered { get; set; }

        /// <summary> The value of the corresponding field in the result of grp_admingetlatestdeleted table. </summary>
        [DataField("username", ReadOnly=true)]
        public string username { get; set; }

        /// <summary> The value of the corresponding field in the result of grp_admingetlatestdeleted table. </summary>
        [DataField("target_date", ReadOnly=true)]
        public DateTime? target_date { get; set; }

        /// <summary> The value of the corresponding field in the result of grp_admingetlatestdeleted table. </summary>
        [DataField("len", ReadOnly=true)]
        public double? len { get; set; }

        /// <summary> The value of the corresponding field in the result of grp_admingetlatestdeleted table. </summary>
        [DataField("description", ReadOnly=true)]
        public string description { get; set; }

        /// <summary> The value of the corresponding field in the result of grp_admingetlatestdeleted table. </summary>
        [DataField("reservation_type", ReadOnly=true)]
        public string reservation_type { get; set; }

        /// <summary> The value of the corresponding field in the result of grp_admingetlatestdeleted table. </summary>
        [DataField("is_deleted", ReadOnly=true)]
        public bool? is_deleted { get; set; }

        /// <summary> The value of the corresponding field in the result of grp_admingetlatestdeleted table. </summary>
        [DataField("deleted", ReadOnly=true)]
        public DateTime? deleted { get; set; }

        /// <summary> The value of the corresponding field in the result of grp_admingetlatestdeleted table. </summary>
        [DataField("id_room", ReadOnly=true)]
        public int? id_room { get; set; }

        /// <summary> The value of the corresponding field in the result of grp_admingetlatestdeleted table. </summary>
        [DataField("is_group", ReadOnly=true)]
        public bool? is_group { get; set; }

        /// <summary> The value of the corresponding field in the result of grp_admingetlatestdeleted table. </summary>
        [DataField("room_name", ReadOnly=true)]
        public string room_name { get; set; }

        #region Columns Struct
        ///<summary>Columns for this table</summary>
        public struct Columns {
            ///<summary>Name of column inside this table</summary>
            public const string entered = @"entered";
            ///<summary>Name of column inside this table</summary>
            public const string username = @"username";
            ///<summary>Name of column inside this table</summary>
            public const string target_date = @"target_date";
            ///<summary>Name of column inside this table</summary>
            public const string len = @"len";
            ///<summary>Name of column inside this table</summary>
            public const string description = @"description";
            ///<summary>Name of column inside this table</summary>
            public const string reservation_type = @"reservation_type";
            ///<summary>Name of column inside this table</summary>
            public const string is_deleted = @"is_deleted";
            ///<summary>Name of column inside this table</summary>
            public const string deleted = @"deleted";
            ///<summary>Name of column inside this table</summary>
            public const string id_room = @"id_room";
            ///<summary>Name of column inside this table</summary>
            public const string is_group = @"is_group";
            ///<summary>Name of column inside this table</summary>
            public const string room_name = @"room_name";
        }
        #endregion
    }
    /// <summary> Wrapper for grp_admingetlatestdeleted. </summary>
    public partial class TableGrpAdmingetlatestdeleted {
        ///<summary>Member</summary>
        public IList<TableGrpAdmingetlatestdeleted_0> Table_0;
    }
    /// <summary> Wrapper for table grp_admingetlatestdynamics. </summary>
    public partial class TableGrpAdmingetlatestdynamics_0 : object {

        /// <summary> The value of the corresponding field in the result of grp_admingetlatestdynamics table. </summary>
        [DataField("d", ReadOnly=true)]
        public DateTime? d { get; set; }

        /// <summary> The value of the corresponding field in the result of grp_admingetlatestdynamics table. </summary>
        [DataField("val_in", ReadOnly=true)]
        public double? val_in { get; set; }

        /// <summary> The value of the corresponding field in the result of grp_admingetlatestdynamics table. </summary>
        [DataField("val_out", ReadOnly=true)]
        public double? val_out { get; set; }

        #region Columns Struct
        ///<summary>Columns for this table</summary>
        public struct Columns {
            ///<summary>Name of column inside this table</summary>
            public const string d = @"d";
            ///<summary>Name of column inside this table</summary>
            public const string val_in = @"val_in";
            ///<summary>Name of column inside this table</summary>
            public const string val_out = @"val_out";
        }
        #endregion
    }
    /// <summary> Wrapper for grp_admingetlatestdynamics. </summary>
    public partial class TableGrpAdmingetlatestdynamics {
        ///<summary>Member</summary>
        public IList<TableGrpAdmingetlatestdynamics_0> Table_0;
    }
    /// <summary> Wrapper for table grp_admingetmonthbyday. </summary>
    public partial class TableGrpAdmingetmonthbyday_0 : object {

        /// <summary> The value of the corresponding field in the result of grp_admingetmonthbyday table. </summary>
        [DataField("d", ReadOnly=true)]
        public DateTime? d { get; set; }

        /// <summary> The value of the corresponding field in the result of grp_admingetmonthbyday table. </summary>
        [DataField("price", ReadOnly=true)]
        public double? price { get; set; }

        /// <summary> The value of the corresponding field in the result of grp_admingetmonthbyday table. </summary>
        [DataField("len", ReadOnly=true)]
        public double? len { get; set; }

        /// <summary> The value of the corresponding field in the result of grp_admingetmonthbyday table. </summary>
        [DataField("price_default", ReadOnly=true)]
        public double? price_default { get; set; }

        #region Columns Struct
        ///<summary>Columns for this table</summary>
        public struct Columns {
            ///<summary>Name of column inside this table</summary>
            public const string d = @"d";
            ///<summary>Name of column inside this table</summary>
            public const string price = @"price";
            ///<summary>Name of column inside this table</summary>
            public const string len = @"len";
            ///<summary>Name of column inside this table</summary>
            public const string price_default = @"price_default";
        }
        #endregion
    }
    /// <summary> Wrapper for grp_admingetmonthbyday. </summary>
    public partial class TableGrpAdmingetmonthbyday {
        ///<summary>Member</summary>
        public IList<TableGrpAdmingetmonthbyday_0> Table_0;
    }
    /// <summary> Wrapper for table grp_admingetmonthbyuserroom. </summary>
    public partial class TableGrpAdmingetmonthbyuserroom_0 : object {

        /// <summary> The value of the corresponding field in the result of grp_admingetmonthbyuserroom table. </summary>
        [DataField("username", ReadOnly=true)]
        public string username { get; set; }

        /// <summary> The value of the corresponding field in the result of grp_admingetmonthbyuserroom table. </summary>
        [DataField("id_room", ReadOnly=true)]
        public int? id_room { get; set; }

        /// <summary> The value of the corresponding field in the result of grp_admingetmonthbyuserroom table. </summary>
        [DataField("user_title", ReadOnly=true)]
        public string user_title { get; set; }

        /// <summary> The value of the corresponding field in the result of grp_admingetmonthbyuserroom table. </summary>
        [DataField("price", ReadOnly=true)]
        public double? price { get; set; }

        /// <summary> The value of the corresponding field in the result of grp_admingetmonthbyuserroom table. </summary>
        [DataField("len", ReadOnly=true)]
        public double? len { get; set; }

        /// <summary> The value of the corresponding field in the result of grp_admingetmonthbyuserroom table. </summary>
        [DataField("price_default", ReadOnly=true)]
        public double? price_default { get; set; }

        /// <summary> The value of the corresponding field in the result of grp_admingetmonthbyuserroom table. </summary>
        [DataField("price_done", ReadOnly=true)]
        public double? price_done { get; set; }

        /// <summary> The value of the corresponding field in the result of grp_admingetmonthbyuserroom table. </summary>
        [DataField("len_done", ReadOnly=true)]
        public double? len_done { get; set; }

        /// <summary> The value of the corresponding field in the result of grp_admingetmonthbyuserroom table. </summary>
        [DataField("price_default_done", ReadOnly=true)]
        public double? price_default_done { get; set; }

        #region Columns Struct
        ///<summary>Columns for this table</summary>
        public struct Columns {
            ///<summary>Name of column inside this table</summary>
            public const string username = @"username";
            ///<summary>Name of column inside this table</summary>
            public const string id_room = @"id_room";
            ///<summary>Name of column inside this table</summary>
            public const string user_title = @"user_title";
            ///<summary>Name of column inside this table</summary>
            public const string price = @"price";
            ///<summary>Name of column inside this table</summary>
            public const string len = @"len";
            ///<summary>Name of column inside this table</summary>
            public const string price_default = @"price_default";
            ///<summary>Name of column inside this table</summary>
            public const string price_done = @"price_done";
            ///<summary>Name of column inside this table</summary>
            public const string len_done = @"len_done";
            ///<summary>Name of column inside this table</summary>
            public const string price_default_done = @"price_default_done";
        }
        #endregion
    }
    /// <summary> Wrapper for grp_admingetmonthbyuserroom. </summary>
    public partial class TableGrpAdmingetmonthbyuserroom {
        ///<summary>Member</summary>
        public IList<TableGrpAdmingetmonthbyuserroom_0> Table_0;
    }
    /// <summary> Wrapper for table grp_adminsearch. </summary>
    public partial class TableGrpAdminsearch_0 : object {

        /// <summary> The value of the corresponding field in the result of grp_adminsearch table. </summary>
        [DataField("user_title", ReadOnly=true)]
        public string user_title { get; set; }

        /// <summary> The value of the corresponding field in the result of grp_adminsearch table. </summary>
        [DataField("id", ReadOnly=true)]
        public int? id { get; set; }

        /// <summary> The value of the corresponding field in the result of grp_adminsearch table. </summary>
        [DataField("username", ReadOnly=true)]
        public string username { get; set; }

        /// <summary> The value of the corresponding field in the result of grp_adminsearch table. </summary>
        [DataField("is_group", ReadOnly=true)]
        public bool? is_group { get; set; }

        /// <summary> The value of the corresponding field in the result of grp_adminsearch table. </summary>
        [DataField("target_date", ReadOnly=true)]
        public DateTime? target_date { get; set; }

        /// <summary> The value of the corresponding field in the result of grp_adminsearch table. </summary>
        [DataField("target_date_end", ReadOnly=true)]
        public DateTime? target_date_end { get; set; }

        /// <summary> The value of the corresponding field in the result of grp_adminsearch table. </summary>
        [DataField("reservation_type", ReadOnly=true)]
        public string reservation_type { get; set; }

        /// <summary> The value of the corresponding field in the result of grp_adminsearch table. </summary>
        [DataField("len", ReadOnly=true)]
        public double? len { get; set; }

        /// <summary> The value of the corresponding field in the result of grp_adminsearch table. </summary>
        [DataField("description", ReadOnly=true)]
        public string description { get; set; }

        /// <summary> The value of the corresponding field in the result of grp_adminsearch table. </summary>
        [DataField("entered", ReadOnly=true)]
        public DateTime? entered { get; set; }

        /// <summary> The value of the corresponding field in the result of grp_adminsearch table. </summary>
        [DataField("last_updated", ReadOnly=true)]
        public DateTime? last_updated { get; set; }

        /// <summary> The value of the corresponding field in the result of grp_adminsearch table. </summary>
        [DataField("deleted", ReadOnly=true)]
        public DateTime? deleted { get; set; }

        /// <summary> The value of the corresponding field in the result of grp_adminsearch table. </summary>
        [DataField("is_deleted", ReadOnly=true)]
        public bool? is_deleted { get; set; }

        /// <summary> The value of the corresponding field in the result of grp_adminsearch table. </summary>
        [DataField("reconciled", ReadOnly=true)]
        public decimal? reconciled { get; set; }

        /// <summary> The value of the corresponding field in the result of grp_adminsearch table. </summary>
        [DataField("id_room", ReadOnly=true)]
        public int? id_room { get; set; }

        /// <summary> The value of the corresponding field in the result of grp_adminsearch table. </summary>
        [DataField("room_name", ReadOnly=true)]
        public string room_name { get; set; }

        /// <summary> The value of the corresponding field in the result of grp_adminsearch table. </summary>
        [DataField("id_pricelist", ReadOnly=true)]
        public int? id_pricelist { get; set; }

        /// <summary> The value of the corresponding field in the result of grp_adminsearch table. </summary>
        [DataField("ex_price", ReadOnly=true)]
        public double? ex_price { get; set; }

        /// <summary> The value of the corresponding field in the result of grp_adminsearch table. </summary>
        [DataField("id_inv", ReadOnly=true)]
        public int? id_inv { get; set; }

        #region Columns Struct
        ///<summary>Columns for this table</summary>
        public struct Columns {
            ///<summary>Name of column inside this table</summary>
            public const string user_title = @"user_title";
            ///<summary>Name of column inside this table</summary>
            public const string id = @"id";
            ///<summary>Name of column inside this table</summary>
            public const string username = @"username";
            ///<summary>Name of column inside this table</summary>
            public const string is_group = @"is_group";
            ///<summary>Name of column inside this table</summary>
            public const string target_date = @"target_date";
            ///<summary>Name of column inside this table</summary>
            public const string target_date_end = @"target_date_end";
            ///<summary>Name of column inside this table</summary>
            public const string reservation_type = @"reservation_type";
            ///<summary>Name of column inside this table</summary>
            public const string len = @"len";
            ///<summary>Name of column inside this table</summary>
            public const string description = @"description";
            ///<summary>Name of column inside this table</summary>
            public const string entered = @"entered";
            ///<summary>Name of column inside this table</summary>
            public const string last_updated = @"last_updated";
            ///<summary>Name of column inside this table</summary>
            public const string deleted = @"deleted";
            ///<summary>Name of column inside this table</summary>
            public const string is_deleted = @"is_deleted";
            ///<summary>Name of column inside this table</summary>
            public const string reconciled = @"reconciled";
            ///<summary>Name of column inside this table</summary>
            public const string id_room = @"id_room";
            ///<summary>Name of column inside this table</summary>
            public const string room_name = @"room_name";
            ///<summary>Name of column inside this table</summary>
            public const string id_pricelist = @"id_pricelist";
            ///<summary>Name of column inside this table</summary>
            public const string ex_price = @"ex_price";
            ///<summary>Name of column inside this table</summary>
            public const string id_inv = @"id_inv";
        }
        #endregion
    }
    /// <summary> Wrapper for grp_adminsearch. </summary>
    public partial class TableGrpAdminsearch {
        ///<summary>Member</summary>
        public IList<TableGrpAdminsearch_0> Table_0;
    }
    /// <summary> Wrapper for table grp_adminsearcharh. </summary>
    public partial class TableGrpAdminsearcharh_0 : object {

        /// <summary> The value of the corresponding field in the result of grp_adminsearcharh table. </summary>
        [DataField("user_title", ReadOnly=true)]
        public string user_title { get; set; }

        /// <summary> The value of the corresponding field in the result of grp_adminsearcharh table. </summary>
        [DataField("id", ReadOnly=true)]
        public int? id { get; set; }

        /// <summary> The value of the corresponding field in the result of grp_adminsearcharh table. </summary>
        [DataField("username", ReadOnly=true)]
        public string username { get; set; }

        /// <summary> The value of the corresponding field in the result of grp_adminsearcharh table. </summary>
        [DataField("is_group", ReadOnly=true)]
        public bool? is_group { get; set; }

        /// <summary> The value of the corresponding field in the result of grp_adminsearcharh table. </summary>
        [DataField("target_date", ReadOnly=true)]
        public DateTime? target_date { get; set; }

        /// <summary> The value of the corresponding field in the result of grp_adminsearcharh table. </summary>
        [DataField("target_date_end", ReadOnly=true)]
        public DateTime? target_date_end { get; set; }

        /// <summary> The value of the corresponding field in the result of grp_adminsearcharh table. </summary>
        [DataField("reservation_type", ReadOnly=true)]
        public string reservation_type { get; set; }

        /// <summary> The value of the corresponding field in the result of grp_adminsearcharh table. </summary>
        [DataField("len", ReadOnly=true)]
        public double? len { get; set; }

        /// <summary> The value of the corresponding field in the result of grp_adminsearcharh table. </summary>
        [DataField("description", ReadOnly=true)]
        public string description { get; set; }

        /// <summary> The value of the corresponding field in the result of grp_adminsearcharh table. </summary>
        [DataField("entered", ReadOnly=true)]
        public DateTime? entered { get; set; }

        /// <summary> The value of the corresponding field in the result of grp_adminsearcharh table. </summary>
        [DataField("last_updated", ReadOnly=true)]
        public DateTime? last_updated { get; set; }

        /// <summary> The value of the corresponding field in the result of grp_adminsearcharh table. </summary>
        [DataField("deleted", ReadOnly=true)]
        public DateTime? deleted { get; set; }

        /// <summary> The value of the corresponding field in the result of grp_adminsearcharh table. </summary>
        [DataField("is_deleted", ReadOnly=true)]
        public bool? is_deleted { get; set; }

        /// <summary> The value of the corresponding field in the result of grp_adminsearcharh table. </summary>
        [DataField("reconciled", ReadOnly=true)]
        public decimal? reconciled { get; set; }

        /// <summary> The value of the corresponding field in the result of grp_adminsearcharh table. </summary>
        [DataField("id_room", ReadOnly=true)]
        public int? id_room { get; set; }

        /// <summary> The value of the corresponding field in the result of grp_adminsearcharh table. </summary>
        [DataField("room_name", ReadOnly=true)]
        public string room_name { get; set; }

        /// <summary> The value of the corresponding field in the result of grp_adminsearcharh table. </summary>
        [DataField("id_pricelist", ReadOnly=true)]
        public int? id_pricelist { get; set; }

        /// <summary> The value of the corresponding field in the result of grp_adminsearcharh table. </summary>
        [DataField("ex_price", ReadOnly=true)]
        public double? ex_price { get; set; }

        /// <summary> The value of the corresponding field in the result of grp_adminsearcharh table. </summary>
        [DataField("id_inv", ReadOnly=true)]
        public int? id_inv { get; set; }

        /// <summary> The value of the corresponding field in the result of grp_adminsearcharh table. </summary>
        [DataField("archived_on", ReadOnly=true)]
        public DateTime? archived_on { get; set; }

        /// <summary> The value of the corresponding field in the result of grp_adminsearcharh table. </summary>
        [DataField("action", ReadOnly=true)]
        public string action { get; set; }

        #region Columns Struct
        ///<summary>Columns for this table</summary>
        public struct Columns {
            ///<summary>Name of column inside this table</summary>
            public const string user_title = @"user_title";
            ///<summary>Name of column inside this table</summary>
            public const string id = @"id";
            ///<summary>Name of column inside this table</summary>
            public const string username = @"username";
            ///<summary>Name of column inside this table</summary>
            public const string is_group = @"is_group";
            ///<summary>Name of column inside this table</summary>
            public const string target_date = @"target_date";
            ///<summary>Name of column inside this table</summary>
            public const string target_date_end = @"target_date_end";
            ///<summary>Name of column inside this table</summary>
            public const string reservation_type = @"reservation_type";
            ///<summary>Name of column inside this table</summary>
            public const string len = @"len";
            ///<summary>Name of column inside this table</summary>
            public const string description = @"description";
            ///<summary>Name of column inside this table</summary>
            public const string entered = @"entered";
            ///<summary>Name of column inside this table</summary>
            public const string last_updated = @"last_updated";
            ///<summary>Name of column inside this table</summary>
            public const string deleted = @"deleted";
            ///<summary>Name of column inside this table</summary>
            public const string is_deleted = @"is_deleted";
            ///<summary>Name of column inside this table</summary>
            public const string reconciled = @"reconciled";
            ///<summary>Name of column inside this table</summary>
            public const string id_room = @"id_room";
            ///<summary>Name of column inside this table</summary>
            public const string room_name = @"room_name";
            ///<summary>Name of column inside this table</summary>
            public const string id_pricelist = @"id_pricelist";
            ///<summary>Name of column inside this table</summary>
            public const string ex_price = @"ex_price";
            ///<summary>Name of column inside this table</summary>
            public const string id_inv = @"id_inv";
            ///<summary>Name of column inside this table</summary>
            public const string archived_on = @"archived_on";
            ///<summary>Name of column inside this table</summary>
            public const string action = @"action";
        }
        #endregion
    }
    /// <summary> Wrapper for grp_adminsearcharh. </summary>
    public partial class TableGrpAdminsearcharh {
        ///<summary>Member</summary>
        public IList<TableGrpAdminsearcharh_0> Table_0;
    }
    /// <summary> Wrapper for table grp_getmultiweek. </summary>
    public partial class TableGrpGetmultiweek_0 : object {

        /// <summary> The value of the corresponding field in the result of grp_getmultiweek table. </summary>
        [DataField("id", ReadOnly=true)]
        public int? id { get; set; }

        /// <summary> The value of the corresponding field in the result of grp_getmultiweek table. </summary>
        [DataField("target_date", ReadOnly=true)]
        public DateTime? target_date { get; set; }

        /// <summary> The value of the corresponding field in the result of grp_getmultiweek table. </summary>
        [DataField("target_date_end", ReadOnly=true)]
        public DateTime? target_date_end { get; set; }

        /// <summary> The value of the corresponding field in the result of grp_getmultiweek table. </summary>
        [DataField("len", ReadOnly=true)]
        public double? len { get; set; }

        /// <summary> The value of the corresponding field in the result of grp_getmultiweek table. </summary>
        [DataField("reservation_type", ReadOnly=true)]
        public string reservation_type { get; set; }

        #region Columns Struct
        ///<summary>Columns for this table</summary>
        public struct Columns {
            ///<summary>Name of column inside this table</summary>
            public const string id = @"id";
            ///<summary>Name of column inside this table</summary>
            public const string target_date = @"target_date";
            ///<summary>Name of column inside this table</summary>
            public const string target_date_end = @"target_date_end";
            ///<summary>Name of column inside this table</summary>
            public const string len = @"len";
            ///<summary>Name of column inside this table</summary>
            public const string reservation_type = @"reservation_type";
        }
        #endregion
    }
    /// <summary> Wrapper for grp_getmultiweek. </summary>
    public partial class TableGrpGetmultiweek {
        ///<summary>Member</summary>
        public IList<TableGrpGetmultiweek_0> Table_0;
    }
    /// <summary> Wrapper for table grp_getoverlaps. </summary>
    public partial class TableGrpGetoverlaps_0 : object {

        /// <summary> The value of the corresponding field in the result of grp_getoverlaps table. </summary>
        [DataField("overlaps", ReadOnly=true)]
        public int? overlaps { get; set; }

        #region Columns Struct
        ///<summary>Columns for this table</summary>
        public struct Columns {
            ///<summary>Name of column inside this table</summary>
            public const string overlaps = @"overlaps";
        }
        #endregion
    }
    /// <summary> Wrapper for grp_getoverlaps. </summary>
    public partial class TableGrpGetoverlaps {
        ///<summary>Member</summary>
        public IList<TableGrpGetoverlaps_0> Table_0;
    }
    /// <summary> Wrapper for table grp_getreservationsforuser. </summary>
    public partial class TableGrpGetreservationsforuser_0 : object {

        /// <summary> The value of the corresponding field in the result of grp_getreservationsforuser table. </summary>
        [DataField("id", ReadOnly=true)]
        public int? id { get; set; }

        /// <summary> The value of the corresponding field in the result of grp_getreservationsforuser table. </summary>
        [DataField("target_date", ReadOnly=true)]
        public DateTime? target_date { get; set; }

        /// <summary> The value of the corresponding field in the result of grp_getreservationsforuser table. </summary>
        [DataField("description", ReadOnly=true)]
        public string description { get; set; }

        /// <summary> The value of the corresponding field in the result of grp_getreservationsforuser table. </summary>
        [DataField("len", ReadOnly=true)]
        public double? len { get; set; }

        /// <summary> The value of the corresponding field in the result of grp_getreservationsforuser table. </summary>
        [DataField("reservation_type", ReadOnly=true)]
        public string reservation_type { get; set; }

        /// <summary> The value of the corresponding field in the result of grp_getreservationsforuser table. </summary>
        [DataField("ex_price", ReadOnly=true)]
        public double? ex_price { get; set; }

        /// <summary> The value of the corresponding field in the result of grp_getreservationsforuser table. </summary>
        [DataField("id_room", ReadOnly=true)]
        public int? id_room { get; set; }

        /// <summary> The value of the corresponding field in the result of grp_getreservationsforuser table. </summary>
        [DataField("room_name", ReadOnly=true)]
        public string room_name { get; set; }

        /// <summary> The value of the corresponding field in the result of grp_getreservationsforuser table. </summary>
        [DataField("is_group", ReadOnly=true)]
        public bool? is_group { get; set; }

        #region Columns Struct
        ///<summary>Columns for this table</summary>
        public struct Columns {
            ///<summary>Name of column inside this table</summary>
            public const string id = @"id";
            ///<summary>Name of column inside this table</summary>
            public const string target_date = @"target_date";
            ///<summary>Name of column inside this table</summary>
            public const string description = @"description";
            ///<summary>Name of column inside this table</summary>
            public const string len = @"len";
            ///<summary>Name of column inside this table</summary>
            public const string reservation_type = @"reservation_type";
            ///<summary>Name of column inside this table</summary>
            public const string ex_price = @"ex_price";
            ///<summary>Name of column inside this table</summary>
            public const string id_room = @"id_room";
            ///<summary>Name of column inside this table</summary>
            public const string room_name = @"room_name";
            ///<summary>Name of column inside this table</summary>
            public const string is_group = @"is_group";
        }
        #endregion
    }
    /// <summary> Wrapper for grp_getreservationsforuser. </summary>
    public partial class TableGrpGetreservationsforuser {
        ///<summary>Member</summary>
        public IList<TableGrpGetreservationsforuser_0> Table_0;
    }
    /// <summary> Wrapper for table grp_getweekview. </summary>
    public partial class TableGrpGetweekview_0 : object {

        /// <summary> The value of the corresponding field in the result of grp_getweekview table. </summary>
        [DataField("description", ReadOnly=true)]
        public string description { get; set; }

        /// <summary> The value of the corresponding field in the result of grp_getweekview table. </summary>
        [DataField("entered", ReadOnly=true)]
        public DateTime? entered { get; set; }

        /// <summary> The value of the corresponding field in the result of grp_getweekview table. </summary>
        [DataField("id", ReadOnly=true)]
        public int? id { get; set; }

        /// <summary> The value of the corresponding field in the result of grp_getweekview table. </summary>
        [DataField("is_group", ReadOnly=true)]
        public bool? is_group { get; set; }

        /// <summary> The value of the corresponding field in the result of grp_getweekview table. </summary>
        [DataField("last_updated", ReadOnly=true)]
        public DateTime? last_updated { get; set; }

        /// <summary> The value of the corresponding field in the result of grp_getweekview table. </summary>
        [DataField("len", ReadOnly=true)]
        public double? len { get; set; }

        /// <summary> The value of the corresponding field in the result of grp_getweekview table. </summary>
        [DataField("reconciled", ReadOnly=true)]
        public decimal? reconciled { get; set; }

        /// <summary> The value of the corresponding field in the result of grp_getweekview table. </summary>
        [DataField("reservation_type", ReadOnly=true)]
        public string reservation_type { get; set; }

        /// <summary> The value of the corresponding field in the result of grp_getweekview table. </summary>
        [DataField("target_date", ReadOnly=true)]
        public DateTime? target_date { get; set; }

        /// <summary> The value of the corresponding field in the result of grp_getweekview table. </summary>
        [DataField("target_date_end", ReadOnly=true)]
        public DateTime? target_date_end { get; set; }

        /// <summary> The value of the corresponding field in the result of grp_getweekview table. </summary>
        [DataField("user_title", ReadOnly=true)]
        public string user_title { get; set; }

        /// <summary> The value of the corresponding field in the result of grp_getweekview table. </summary>
        [DataField("username", ReadOnly=true)]
        public string username { get; set; }

        /// <summary> The value of the corresponding field in the result of grp_getweekview table. </summary>
        [DataField("ex_price", ReadOnly=true)]
        public double? ex_price { get; set; }

        #region Columns Struct
        ///<summary>Columns for this table</summary>
        public struct Columns {
            ///<summary>Name of column inside this table</summary>
            public const string description = @"description";
            ///<summary>Name of column inside this table</summary>
            public const string entered = @"entered";
            ///<summary>Name of column inside this table</summary>
            public const string id = @"id";
            ///<summary>Name of column inside this table</summary>
            public const string is_group = @"is_group";
            ///<summary>Name of column inside this table</summary>
            public const string last_updated = @"last_updated";
            ///<summary>Name of column inside this table</summary>
            public const string len = @"len";
            ///<summary>Name of column inside this table</summary>
            public const string reconciled = @"reconciled";
            ///<summary>Name of column inside this table</summary>
            public const string reservation_type = @"reservation_type";
            ///<summary>Name of column inside this table</summary>
            public const string target_date = @"target_date";
            ///<summary>Name of column inside this table</summary>
            public const string target_date_end = @"target_date_end";
            ///<summary>Name of column inside this table</summary>
            public const string user_title = @"user_title";
            ///<summary>Name of column inside this table</summary>
            public const string username = @"username";
            ///<summary>Name of column inside this table</summary>
            public const string ex_price = @"ex_price";
        }
        #endregion
    }
    /// <summary> Wrapper for grp_getweekview. </summary>
    public partial class TableGrpGetweekview {
        ///<summary>Member</summary>
        public IList<TableGrpGetweekview_0> Table_0;
    }
    /// <summary> Wrapper for table grp_glbalance. </summary>
    public partial class TableGrpGlbalance_0 : object {

        /// <summary> The value of the corresponding field in the result of grp_glbalance table. </summary>
        [DataField("bal", ReadOnly=true)]
        public decimal? bal { get; set; }

        #region Columns Struct
        ///<summary>Columns for this table</summary>
        public struct Columns {
            ///<summary>Name of column inside this table</summary>
            public const string bal = @"bal";
        }
        #endregion
    }
    /// <summary> Wrapper for grp_glbalance. </summary>
    public partial class TableGrpGlbalance {
        ///<summary>Member</summary>
        public IList<TableGrpGlbalance_0> Table_0;
    }



#endregion
    }

    /// <summary> Public interface containing stored-procedure wrappers. </summary>
    public partial interface I {
        #region grp_admingetdeletedstats
        /// <summary> Wrapper for stored procedure grp_admingetdeletedstats. </summary>
        DataTable grp_admingetdeletedstats(DateTime? from, DateTime? to);
        /// <summary> Wrapper for stored procedure grp_admingetdeletedstats returning DataSet. </summary>
        DataSet grp_admingetdeletedstats_DataSet(DateTime? from, DateTime? to);
        /// <summary> Wrapper for stored procedure grp_admingetdeletedstats returning data object. </summary>
        .TableGrpAdmingetdeletedstats grp_admingetdeletedstats_DataObject(DateTime? from, DateTime? to);
        #endregion
        #region grp_admingetinvoiceitems
        /// <summary> Wrapper for stored procedure grp_admingetinvoiceitems. </summary>
        DataTable grp_admingetinvoiceitems(int? id);
        /// <summary> Wrapper for stored procedure grp_admingetinvoiceitems returning DataSet. </summary>
        DataSet grp_admingetinvoiceitems_DataSet(int? id);
        /// <summary> Wrapper for stored procedure grp_admingetinvoiceitems returning data object. </summary>
        .TableGrpAdmingetinvoiceitems grp_admingetinvoiceitems_DataObject(int? id);
        #endregion
        #region grp_admingetlatest
        /// <summary> Wrapper for stored procedure grp_admingetlatest. </summary>
        DataTable grp_admingetlatest();
        /// <summary> Wrapper for stored procedure grp_admingetlatest returning DataSet. </summary>
        DataSet grp_admingetlatest_DataSet();
        /// <summary> Wrapper for stored procedure grp_admingetlatest returning data object. </summary>
        .TableGrpAdmingetlatest grp_admingetlatest_DataObject();
        #endregion
        #region grp_admingetlatestdeleted
        /// <summary> Wrapper for stored procedure grp_admingetlatestdeleted. </summary>
        DataTable grp_admingetlatestdeleted();
        /// <summary> Wrapper for stored procedure grp_admingetlatestdeleted returning DataSet. </summary>
        DataSet grp_admingetlatestdeleted_DataSet();
        /// <summary> Wrapper for stored procedure grp_admingetlatestdeleted returning data object. </summary>
        .TableGrpAdmingetlatestdeleted grp_admingetlatestdeleted_DataObject();
        #endregion
        #region grp_admingetlatestdynamics
        /// <summary> Wrapper for stored procedure grp_admingetlatestdynamics. </summary>
        DataTable grp_admingetlatestdynamics();
        /// <summary> Wrapper for stored procedure grp_admingetlatestdynamics returning DataSet. </summary>
        DataSet grp_admingetlatestdynamics_DataSet();
        /// <summary> Wrapper for stored procedure grp_admingetlatestdynamics returning data object. </summary>
        .TableGrpAdmingetlatestdynamics grp_admingetlatestdynamics_DataObject();
        #endregion
        #region grp_admingetmonthbyday
        /// <summary> Wrapper for stored procedure grp_admingetmonthbyday. </summary>
        DataTable grp_admingetmonthbyday(DateTime? d1, DateTime? d2);
        /// <summary> Wrapper for stored procedure grp_admingetmonthbyday returning DataSet. </summary>
        DataSet grp_admingetmonthbyday_DataSet(DateTime? d1, DateTime? d2);
        /// <summary> Wrapper for stored procedure grp_admingetmonthbyday returning data object. </summary>
        .TableGrpAdmingetmonthbyday grp_admingetmonthbyday_DataObject(DateTime? d1, DateTime? d2);
        #endregion
        #region grp_admingetmonthbyuserroom
        /// <summary> Wrapper for stored procedure grp_admingetmonthbyuserroom. </summary>
        DataTable grp_admingetmonthbyuserroom(DateTime? d1, DateTime? d2, bool? only_new, string username);
        /// <summary> Wrapper for stored procedure grp_admingetmonthbyuserroom returning DataSet. </summary>
        DataSet grp_admingetmonthbyuserroom_DataSet(DateTime? d1, DateTime? d2, bool? only_new, string username);
        /// <summary> Wrapper for stored procedure grp_admingetmonthbyuserroom returning data object. </summary>
        .TableGrpAdmingetmonthbyuserroom grp_admingetmonthbyuserroom_DataObject(DateTime? d1, DateTime? d2, bool? only_new, string username);
        #endregion
        #region grp_adminsearch
        /// <summary> Wrapper for stored procedure grp_adminsearch. </summary>
        DataTable grp_adminsearch(string username, int? r, DateTime? start, DateTime? end, string deleted);
        /// <summary> Wrapper for stored procedure grp_adminsearch returning DataSet. </summary>
        DataSet grp_adminsearch_DataSet(string username, int? r, DateTime? start, DateTime? end, string deleted);
        /// <summary> Wrapper for stored procedure grp_adminsearch returning data object. </summary>
        .TableGrpAdminsearch grp_adminsearch_DataObject(string username, int? r, DateTime? start, DateTime? end, string deleted);
        #endregion
        #region grp_adminsearcharh
        /// <summary> Wrapper for stored procedure grp_adminsearcharh. </summary>
        DataTable grp_adminsearcharh(string username, int? r, DateTime? start, DateTime? end);
        /// <summary> Wrapper for stored procedure grp_adminsearcharh returning DataSet. </summary>
        DataSet grp_adminsearcharh_DataSet(string username, int? r, DateTime? start, DateTime? end);
        /// <summary> Wrapper for stored procedure grp_adminsearcharh returning data object. </summary>
        .TableGrpAdminsearcharh grp_adminsearcharh_DataObject(string username, int? r, DateTime? start, DateTime? end);
        #endregion
        #region grp_getmultiweek
        /// <summary> Wrapper for stored procedure grp_getmultiweek. </summary>
        DataTable grp_getmultiweek(int? id_room, int? window);
        /// <summary> Wrapper for stored procedure grp_getmultiweek returning DataSet. </summary>
        DataSet grp_getmultiweek_DataSet(int? id_room, int? window);
        /// <summary> Wrapper for stored procedure grp_getmultiweek returning data object. </summary>
        .TableGrpGetmultiweek grp_getmultiweek_DataObject(int? id_room, int? window);
        #endregion
        #region grp_getoverlaps
        /// <summary> Wrapper for stored procedure grp_getoverlaps. </summary>
        DataTable grp_getoverlaps(int? id_room, DateTime? d1, DateTime? d2, string u, int? id);
        /// <summary> Wrapper for stored procedure grp_getoverlaps returning DataSet. </summary>
        DataSet grp_getoverlaps_DataSet(int? id_room, DateTime? d1, DateTime? d2, string u, int? id);
        /// <summary> Wrapper for stored procedure grp_getoverlaps returning data object. </summary>
        .TableGrpGetoverlaps grp_getoverlaps_DataObject(int? id_room, DateTime? d1, DateTime? d2, string u, int? id);
        #endregion
        #region grp_getreservationsforuser
        /// <summary> Wrapper for stored procedure grp_getreservationsforuser. </summary>
        DataTable grp_getreservationsforuser(string u, DateTime? d1, DateTime? d2);
        /// <summary> Wrapper for stored procedure grp_getreservationsforuser returning DataSet. </summary>
        DataSet grp_getreservationsforuser_DataSet(string u, DateTime? d1, DateTime? d2);
        /// <summary> Wrapper for stored procedure grp_getreservationsforuser returning data object. </summary>
        .TableGrpGetreservationsforuser grp_getreservationsforuser_DataObject(string u, DateTime? d1, DateTime? d2);
        #endregion
        #region grp_getweekview
        /// <summary> Wrapper for stored procedure grp_getweekview. </summary>
        DataTable grp_getweekview(int? id_room, int? offset);
        /// <summary> Wrapper for stored procedure grp_getweekview returning DataSet. </summary>
        DataSet grp_getweekview_DataSet(int? id_room, int? offset);
        /// <summary> Wrapper for stored procedure grp_getweekview returning data object. </summary>
        .TableGrpGetweekview grp_getweekview_DataObject(int? id_room, int? offset);
        #endregion
        #region grp_glbalance
        /// <summary> Wrapper for stored procedure grp_glbalance. </summary>
        DataTable grp_glbalance(string username, DateTime? start, DateTime? end, string acc_name);
        /// <summary> Wrapper for stored procedure grp_glbalance returning DataSet. </summary>
        DataSet grp_glbalance_DataSet(string username, DateTime? start, DateTime? end, string acc_name);
        /// <summary> Wrapper for stored procedure grp_glbalance returning data object. </summary>
        .TableGrpGlbalance grp_glbalance_DataObject(string username, DateTime? start, DateTime? end, string acc_name);
        #endregion
        #region gsp_adminassigntoinvoice
        /// <summary> Wrapper for stored procedure gsp_adminassigntoinvoice. </summary>
        int gsp_adminassigntoinvoice(DateTime? d1, DateTime? d2, string username, int? id_inv);
        #endregion
        #region gsp_admincancelpayment
        /// <summary> Wrapper for stored procedure gsp_admincancelpayment. </summary>
        int gsp_admincancelpayment(int? payment_id);
        #endregion
        #region gsp_admindeleteinvoice
        /// <summary> Wrapper for stored procedure gsp_admindeleteinvoice. </summary>
        int gsp_admindeleteinvoice(int? id);
        #endregion
        #region gsp_adminfixprices
        /// <summary> Wrapper for stored procedure gsp_adminfixprices. </summary>
        int gsp_adminfixprices(DateTime? d);
        #endregion
        #region gsp_adminfixpricesforinvoice
        /// <summary> Wrapper for stored procedure gsp_adminfixpricesforinvoice. </summary>
        int gsp_adminfixpricesforinvoice(int? id_inv);
        #endregion
        #region trp_gettablestats
        /// <summary> Wrapper for stored procedure trp_gettablestats. </summary>
        int trp_gettablestats();
        #endregion
        #region tsp_db_tables_size
        /// <summary> Wrapper for stored procedure tsp_db_tables_size. </summary>
        int tsp_db_tables_size(bool? Sort);
        #endregion
        #region tsp_generate_inserts
        /// <summary> Wrapper for stored procedure tsp_generate_inserts. </summary>
        int tsp_generate_inserts(string t_name, string o_name, string i_iden, string d_table, string where_stmt, bool? append);
        #endregion
        #region tsp_recompileallobjects
        /// <summary> Wrapper for stored procedure tsp_recompileallobjects. </summary>
        int tsp_recompileallobjects();
        #endregion
    }

    /// <summary> Public class containing stored-procedure wrappers. </summary>
    public partial class Obj :  I {

        protected IDataProviderSp provider;
        public Obj(IDataProviderSp _provider) { this.provider = _provider; }

		/// <summary>
		/// this method checks is specified data within provided object is null
		/// </summary>
		/// <param name="o">object containing data</param>
		/// <returns>true if object points to null</returns>
		static bool CheckNull(object o){
			if (o is INullable) return (o as INullable).IsNull;
			if (o is string) return (o as string) == null;
			if (o is Array) return (o as Array) == null;
			return o == null;
		}
		
        /// <summary>
		/// this method creates new parameter
		/// </summary>
		static IDbDataParameter AddParameter(IDbCommand cmd, string name, DbType type, object val) {
            IDbDataParameter par = cmd.CreateParameter();
            par.DbType = type;
            par.ParameterName = name;
            par.Value = val;
            cmd.Parameters.Add(par);
            return par;
        }

        /// <summary>
		/// this method creates new output parameter
		/// </summary>
        static IDbDataParameter AddParameterOut(IDbCommand cmd, string name, DbType type, object val, byte? precision, byte? scale, int? size) {
            IDbDataParameter par = cmd.CreateParameter();
            par.DbType = type;
            par.Direction = ParameterDirection.InputOutput;
            par.ParameterName = name;
            par.Value = val;
            if (precision.HasValue) {
                par.Precision = precision.Value;
                par.Scale = scale.Value;
            }
            if (size.HasValue)
                par.Size = size.Value;
            cmd.Parameters.Add(par);
            return par;
        }
        #region grp_admingetdeletedstats
        /// <summary> Wrapper for stored procedure grp_admingetdeletedstats. </summary>
        public DataTable grp_admingetdeletedstats(DateTime? from, DateTime? to) { 
            return .grp_admingetdeletedstats(provider, from, to);
        }
        /// <summary> Wrapper for stored procedure grp_admingetdeletedstats returning DataSet. </summary>
        public DataSet grp_admingetdeletedstats_DataSet(DateTime? from, DateTime? to) { 
            return .grp_admingetdeletedstats_DataSet(provider, from, to);
        }
        /// <summary> Wrapper for stored procedure grp_admingetdeletedstats returning data object. </summary>
        public .TableGrpAdmingetdeletedstats grp_admingetdeletedstats_DataObject(DateTime? from, DateTime? to) {
            return .grp_admingetdeletedstats_DataObject(provider, from, to);
        }
        #endregion
        #region grp_admingetinvoiceitems
        /// <summary> Wrapper for stored procedure grp_admingetinvoiceitems. </summary>
        public DataTable grp_admingetinvoiceitems(int? id) { 
            return .grp_admingetinvoiceitems(provider, id);
        }
        /// <summary> Wrapper for stored procedure grp_admingetinvoiceitems returning DataSet. </summary>
        public DataSet grp_admingetinvoiceitems_DataSet(int? id) { 
            return .grp_admingetinvoiceitems_DataSet(provider, id);
        }
        /// <summary> Wrapper for stored procedure grp_admingetinvoiceitems returning data object. </summary>
        public .TableGrpAdmingetinvoiceitems grp_admingetinvoiceitems_DataObject(int? id) {
            return .grp_admingetinvoiceitems_DataObject(provider, id);
        }
        #endregion
        #region grp_admingetlatest
        /// <summary> Wrapper for stored procedure grp_admingetlatest. </summary>
        public DataTable grp_admingetlatest() { 
            return .grp_admingetlatest(provider);
        }
        /// <summary> Wrapper for stored procedure grp_admingetlatest returning DataSet. </summary>
        public DataSet grp_admingetlatest_DataSet() { 
            return .grp_admingetlatest_DataSet(provider);
        }
        /// <summary> Wrapper for stored procedure grp_admingetlatest returning data object. </summary>
        public .TableGrpAdmingetlatest grp_admingetlatest_DataObject() {
            return .grp_admingetlatest_DataObject(provider);
        }
        #endregion
        #region grp_admingetlatestdeleted
        /// <summary> Wrapper for stored procedure grp_admingetlatestdeleted. </summary>
        public DataTable grp_admingetlatestdeleted() { 
            return .grp_admingetlatestdeleted(provider);
        }
        /// <summary> Wrapper for stored procedure grp_admingetlatestdeleted returning DataSet. </summary>
        public DataSet grp_admingetlatestdeleted_DataSet() { 
            return .grp_admingetlatestdeleted_DataSet(provider);
        }
        /// <summary> Wrapper for stored procedure grp_admingetlatestdeleted returning data object. </summary>
        public .TableGrpAdmingetlatestdeleted grp_admingetlatestdeleted_DataObject() {
            return .grp_admingetlatestdeleted_DataObject(provider);
        }
        #endregion
        #region grp_admingetlatestdynamics
        /// <summary> Wrapper for stored procedure grp_admingetlatestdynamics. </summary>
        public DataTable grp_admingetlatestdynamics() { 
            return .grp_admingetlatestdynamics(provider);
        }
        /// <summary> Wrapper for stored procedure grp_admingetlatestdynamics returning DataSet. </summary>
        public DataSet grp_admingetlatestdynamics_DataSet() { 
            return .grp_admingetlatestdynamics_DataSet(provider);
        }
        /// <summary> Wrapper for stored procedure grp_admingetlatestdynamics returning data object. </summary>
        public .TableGrpAdmingetlatestdynamics grp_admingetlatestdynamics_DataObject() {
            return .grp_admingetlatestdynamics_DataObject(provider);
        }
        #endregion
        #region grp_admingetmonthbyday
        /// <summary> Wrapper for stored procedure grp_admingetmonthbyday. </summary>
        public DataTable grp_admingetmonthbyday(DateTime? d1, DateTime? d2) { 
            return .grp_admingetmonthbyday(provider, d1, d2);
        }
        /// <summary> Wrapper for stored procedure grp_admingetmonthbyday returning DataSet. </summary>
        public DataSet grp_admingetmonthbyday_DataSet(DateTime? d1, DateTime? d2) { 
            return .grp_admingetmonthbyday_DataSet(provider, d1, d2);
        }
        /// <summary> Wrapper for stored procedure grp_admingetmonthbyday returning data object. </summary>
        public .TableGrpAdmingetmonthbyday grp_admingetmonthbyday_DataObject(DateTime? d1, DateTime? d2) {
            return .grp_admingetmonthbyday_DataObject(provider, d1, d2);
        }
        #endregion
        #region grp_admingetmonthbyuserroom
        /// <summary> Wrapper for stored procedure grp_admingetmonthbyuserroom. </summary>
        public DataTable grp_admingetmonthbyuserroom(DateTime? d1, DateTime? d2, bool? only_new, string username) { 
            return .grp_admingetmonthbyuserroom(provider, d1, d2, only_new, username);
        }
        /// <summary> Wrapper for stored procedure grp_admingetmonthbyuserroom returning DataSet. </summary>
        public DataSet grp_admingetmonthbyuserroom_DataSet(DateTime? d1, DateTime? d2, bool? only_new, string username) { 
            return .grp_admingetmonthbyuserroom_DataSet(provider, d1, d2, only_new, username);
        }
        /// <summary> Wrapper for stored procedure grp_admingetmonthbyuserroom returning data object. </summary>
        public .TableGrpAdmingetmonthbyuserroom grp_admingetmonthbyuserroom_DataObject(DateTime? d1, DateTime? d2, bool? only_new, string username) {
            return .grp_admingetmonthbyuserroom_DataObject(provider, d1, d2, only_new, username);
        }
        #endregion
        #region grp_adminsearch
        /// <summary> Wrapper for stored procedure grp_adminsearch. </summary>
        public DataTable grp_adminsearch(string username, int? r, DateTime? start, DateTime? end, string deleted) { 
            return .grp_adminsearch(provider, username, r, start, end, deleted);
        }
        /// <summary> Wrapper for stored procedure grp_adminsearch returning DataSet. </summary>
        public DataSet grp_adminsearch_DataSet(string username, int? r, DateTime? start, DateTime? end, string deleted) { 
            return .grp_adminsearch_DataSet(provider, username, r, start, end, deleted);
        }
        /// <summary> Wrapper for stored procedure grp_adminsearch returning data object. </summary>
        public .TableGrpAdminsearch grp_adminsearch_DataObject(string username, int? r, DateTime? start, DateTime? end, string deleted) {
            return .grp_adminsearch_DataObject(provider, username, r, start, end, deleted);
        }
        #endregion
        #region grp_adminsearcharh
        /// <summary> Wrapper for stored procedure grp_adminsearcharh. </summary>
        public DataTable grp_adminsearcharh(string username, int? r, DateTime? start, DateTime? end) { 
            return .grp_adminsearcharh(provider, username, r, start, end);
        }
        /// <summary> Wrapper for stored procedure grp_adminsearcharh returning DataSet. </summary>
        public DataSet grp_adminsearcharh_DataSet(string username, int? r, DateTime? start, DateTime? end) { 
            return .grp_adminsearcharh_DataSet(provider, username, r, start, end);
        }
        /// <summary> Wrapper for stored procedure grp_adminsearcharh returning data object. </summary>
        public .TableGrpAdminsearcharh grp_adminsearcharh_DataObject(string username, int? r, DateTime? start, DateTime? end) {
            return .grp_adminsearcharh_DataObject(provider, username, r, start, end);
        }
        #endregion
        #region grp_getmultiweek
        /// <summary> Wrapper for stored procedure grp_getmultiweek. </summary>
        public DataTable grp_getmultiweek(int? id_room, int? window) { 
            return .grp_getmultiweek(provider, id_room, window);
        }
        /// <summary> Wrapper for stored procedure grp_getmultiweek returning DataSet. </summary>
        public DataSet grp_getmultiweek_DataSet(int? id_room, int? window) { 
            return .grp_getmultiweek_DataSet(provider, id_room, window);
        }
        /// <summary> Wrapper for stored procedure grp_getmultiweek returning data object. </summary>
        public .TableGrpGetmultiweek grp_getmultiweek_DataObject(int? id_room, int? window) {
            return .grp_getmultiweek_DataObject(provider, id_room, window);
        }
        #endregion
        #region grp_getoverlaps
        /// <summary> Wrapper for stored procedure grp_getoverlaps. </summary>
        public DataTable grp_getoverlaps(int? id_room, DateTime? d1, DateTime? d2, string u, int? id) { 
            return .grp_getoverlaps(provider, id_room, d1, d2, u, id);
        }
        /// <summary> Wrapper for stored procedure grp_getoverlaps returning DataSet. </summary>
        public DataSet grp_getoverlaps_DataSet(int? id_room, DateTime? d1, DateTime? d2, string u, int? id) { 
            return .grp_getoverlaps_DataSet(provider, id_room, d1, d2, u, id);
        }
        /// <summary> Wrapper for stored procedure grp_getoverlaps returning data object. </summary>
        public .TableGrpGetoverlaps grp_getoverlaps_DataObject(int? id_room, DateTime? d1, DateTime? d2, string u, int? id) {
            return .grp_getoverlaps_DataObject(provider, id_room, d1, d2, u, id);
        }
        #endregion
        #region grp_getreservationsforuser
        /// <summary> Wrapper for stored procedure grp_getreservationsforuser. </summary>
        public DataTable grp_getreservationsforuser(string u, DateTime? d1, DateTime? d2) { 
            return .grp_getreservationsforuser(provider, u, d1, d2);
        }
        /// <summary> Wrapper for stored procedure grp_getreservationsforuser returning DataSet. </summary>
        public DataSet grp_getreservationsforuser_DataSet(string u, DateTime? d1, DateTime? d2) { 
            return .grp_getreservationsforuser_DataSet(provider, u, d1, d2);
        }
        /// <summary> Wrapper for stored procedure grp_getreservationsforuser returning data object. </summary>
        public .TableGrpGetreservationsforuser grp_getreservationsforuser_DataObject(string u, DateTime? d1, DateTime? d2) {
            return .grp_getreservationsforuser_DataObject(provider, u, d1, d2);
        }
        #endregion
        #region grp_getweekview
        /// <summary> Wrapper for stored procedure grp_getweekview. </summary>
        public DataTable grp_getweekview(int? id_room, int? offset) { 
            return .grp_getweekview(provider, id_room, offset);
        }
        /// <summary> Wrapper for stored procedure grp_getweekview returning DataSet. </summary>
        public DataSet grp_getweekview_DataSet(int? id_room, int? offset) { 
            return .grp_getweekview_DataSet(provider, id_room, offset);
        }
        /// <summary> Wrapper for stored procedure grp_getweekview returning data object. </summary>
        public .TableGrpGetweekview grp_getweekview_DataObject(int? id_room, int? offset) {
            return .grp_getweekview_DataObject(provider, id_room, offset);
        }
        #endregion
        #region grp_glbalance
        /// <summary> Wrapper for stored procedure grp_glbalance. </summary>
        public DataTable grp_glbalance(string username, DateTime? start, DateTime? end, string acc_name) { 
            return .grp_glbalance(provider, username, start, end, acc_name);
        }
        /// <summary> Wrapper for stored procedure grp_glbalance returning DataSet. </summary>
        public DataSet grp_glbalance_DataSet(string username, DateTime? start, DateTime? end, string acc_name) { 
            return .grp_glbalance_DataSet(provider, username, start, end, acc_name);
        }
        /// <summary> Wrapper for stored procedure grp_glbalance returning data object. </summary>
        public .TableGrpGlbalance grp_glbalance_DataObject(string username, DateTime? start, DateTime? end, string acc_name) {
            return .grp_glbalance_DataObject(provider, username, start, end, acc_name);
        }
        #endregion
        #region gsp_adminassigntoinvoice
        /// <summary> Wrapper for stored procedure gsp_adminassigntoinvoice. </summary>
        public int gsp_adminassigntoinvoice(DateTime? d1, DateTime? d2, string username, int? id_inv) { 
            return .gsp_adminassigntoinvoice(provider, d1, d2, username, id_inv);
        }
        #endregion
        #region gsp_admincancelpayment
        /// <summary> Wrapper for stored procedure gsp_admincancelpayment. </summary>
        public int gsp_admincancelpayment(int? payment_id) { 
            return .gsp_admincancelpayment(provider, payment_id);
        }
        #endregion
        #region gsp_admindeleteinvoice
        /// <summary> Wrapper for stored procedure gsp_admindeleteinvoice. </summary>
        public int gsp_admindeleteinvoice(int? id) { 
            return .gsp_admindeleteinvoice(provider, id);
        }
        #endregion
        #region gsp_adminfixprices
        /// <summary> Wrapper for stored procedure gsp_adminfixprices. </summary>
        public int gsp_adminfixprices(DateTime? d) { 
            return .gsp_adminfixprices(provider, d);
        }
        #endregion
        #region gsp_adminfixpricesforinvoice
        /// <summary> Wrapper for stored procedure gsp_adminfixpricesforinvoice. </summary>
        public int gsp_adminfixpricesforinvoice(int? id_inv) { 
            return .gsp_adminfixpricesforinvoice(provider, id_inv);
        }
        #endregion
        #region trp_gettablestats
        /// <summary> Wrapper for stored procedure trp_gettablestats. </summary>
        public int trp_gettablestats() { 
            return .trp_gettablestats(provider);
        }
        #endregion
        #region tsp_db_tables_size
        /// <summary> Wrapper for stored procedure tsp_db_tables_size. </summary>
        public int tsp_db_tables_size(bool? Sort) { 
            return .tsp_db_tables_size(provider, Sort);
        }
        #endregion
        #region tsp_generate_inserts
        /// <summary> Wrapper for stored procedure tsp_generate_inserts. </summary>
        public int tsp_generate_inserts(string t_name, string o_name, string i_iden, string d_table, string where_stmt, bool? append) { 
            return .tsp_generate_inserts(provider, t_name, o_name, i_iden, d_table, where_stmt, append);
        }
        #endregion
        #region tsp_recompileallobjects
        /// <summary> Wrapper for stored procedure tsp_recompileallobjects. </summary>
        public int tsp_recompileallobjects() { 
            return .tsp_recompileallobjects(provider);
        }
        #endregion
    }

    #region db context

    public interface IDbContext {
    }

    partial class DbContext : IDbContext {
        private IQToolkit.IEntityProvider provider;
        public DbContext(IQToolkit.IEntityProvider provider) { this.provider = provider; }
    }



    public partial class DbContextMemory : IDbContext {
    }


    #endregion
}
