using System;
using System.Web;
using System.Data;
using Microsoft.Data.SqlClient;
using System.Data.SqlTypes;
using System.Xml;
using System.Xml.Serialization;
using System.IO;
using System.Collections;
using System.Reflection;
using System.Text;

namespace DalGenerator {

    /////////////////////////////////////////////////////////////////////////////
    /// <summary>
    /// This is a utility class containing utility functions for many small tasks.
    /// </summary>
    /////////////////////////////////////////////////////////////////////////////

    public class Utils {

        #region loading of resources

        public static string LoadFileRelative(string rel_path) {
            var s = Path.Combine(Path.GetDirectoryName(Assembly.GetEntryAssembly().Location), rel_path);
            return File.ReadAllText(s);
        }

        /// <summary> This method loads embedded resource as string </summary>
        /// <param name="resource_name"> Name of the resource (namespace prefix is ignored) </param>
        /// <returns> Resource as string </returns>

        public static string LoadEmbededResourceAsString(string resource_name) {
            return LoadEmbededResourceAsString(resource_name, Assembly.GetCallingAssembly());
        }

        /// <summary> This method loads embedded resource as string </summary>
        /// <param name="resource_name"> Name of the resource (namespace prefix is ignored) </param>
        /// <param name="a"> reference to assembly that is to be searched </param>
        /// <returns> Resource as string </returns>

        public static string LoadEmbededResourceAsString(string resource_name, Assembly a) {
            Stream res_stream = null;
            string res = "";

            // get a list of resource names from the manifest
            string[] res_names = a.GetManifestResourceNames();

            foreach (string s in res_names) {
                if (s.EndsWith("." + resource_name) || s == resource_name) {
                    res_stream = a.GetManifestResourceStream(s);
                    if (res_stream != null) {
                        try {
                            using (StreamReader sr = new StreamReader(res_stream)) {
                                res = sr.ReadToEnd();
                            }
                        } finally { res_stream.Close(); }
                    }
                }
            }
            return res;
        }

        #endregion

        #region database interraction

        /// <summary> This method returns DataTable that is the
        /// result of given SQL command </summary>
        public static DataTable GetDataTab(SqlConnection con, string sql_cmd) {
            SqlDataAdapter da = new SqlDataAdapter(sql_cmd, con);
            DataTable tab = new DataTable();
            da.Fill(tab);
            return tab;
        }

        /// <summary> This method returns DataSet that is the
        /// result of given SQL command </summary>
        public static DataSet GetDataSet(SqlCommand cmd) {
            SqlDataAdapter da = new SqlDataAdapter(cmd);
            var ds = new DataSet();
            da.Fill(ds);
            return ds;
        }

        /// <summary> This method executes given sql command
        /// that does not return any data </summary>
        public static void ExecuteNonQuery(SqlConnection con, string sql_cmd) {
            SqlCommand cmd = con.CreateCommand();
            cmd.CommandText = sql_cmd;
            cmd.ExecuteNonQuery();
        }

        /// <summary>
        /// This method opens connection to specified server and database using Integrated security.
        /// </summary>
        /// <param name="server_name"></param>
        /// <param name="db_name"></param>
        /// <returns></returns>
        public static SqlConnection CreateConnection(string server_name, string db_name) {
            string res = "Data Source={0};Initial Catalog={1};Integrated Security=SSPI;";
            res = string.Format(res, server_name, db_name);

            SqlConnection con = new SqlConnection(res);
            con.Open();
            return con;
        }

        /// <summary>
        /// This method opens connection to specified server and database using Integrated security.
        /// </summary>
        /// <param name="server_name"></param>
        /// <param name="db_name"></param>
        /// <param name="p"></param>
        /// <param name="u"></param>
        /// <returns></returns>
        public static SqlConnection CreateConnection(string server_name, string db_name, string u, string p) {
            string res = "Data Source={0};Initial Catalog={1};uid={2};pwd={3}";
            res = string.Format(res, server_name, db_name, u, p);

            SqlConnection con = new SqlConnection(res);
            con.Open();
            return con;
        }

        #endregion

        #region casing helper

        /// <summary>
        /// This method creates PascalCasing from given string
        /// </summary>
        /// <param name="name">name of table/stored proc</param>
        /// <returns>PascalCased name</returns>
        public static string PascalCasing(string name) {
            bool should_uppercase = true;
            string res = "";
            for (int i = 0; i < name.Length; i++) {
                if (name[i] == '_') {
                    if (i > 0) {
                        should_uppercase = true;
                    } else {
                        res += "_";
                        should_uppercase = true;
                    }
                } else if (should_uppercase) {
                    res += name[i].ToString().ToUpper();
                    should_uppercase = false;
                } else {
                    res += name[i].ToString().ToLower();
                }
            }
            return res;
        }

        #endregion

        #region utility functions for getting meta-data from database

        /// <summary>
        /// This method fetches list of all tables
        /// </summary>
        public static DataTable GetTables(SqlConnection con) {
            return GetDataTab(con, @"
				SELECT
					--UPPER(name) AS table_name,
                    name AS table_name,
					cast(0 as bit) as selected
				FROM  dbo.sysobjects
				WHERE (type = 'U') ORDER BY UPPER(name)");
        }

        /// <summary>
        /// This method fetches list of all tables
        /// </summary>
        public static DataTable GetViews(SqlConnection con) {
            return GetDataTab(con, @"
				SELECT
					--UPPER(name) AS table_name,
                    name AS table_name,
					cast(0 as bit) as selected
				FROM  dbo.sysobjects
				WHERE (type = 'V') ORDER BY UPPER(name)");
        }

        /// <summary>
        /// This method fetches list of all stored procedures
        /// </summary>
        public static DataTable GetSPs(SqlConnection con) {
            return GetDataTab(con, @"
				SELECT ROUTINE_NAME as sp_name
				FROM INFORMATION_SCHEMA.ROUTINES
				WHERE (ROUTINE_TYPE = 'PROCEDURE')
				ORDER BY ROUTINE_NAME");
        }

        /// <summary>
        /// This method fetches list of primary keys for specified table
        /// </summary>
        public static DataTable GetPrimaryKeysForTable(SqlConnection con, string table_name) {
            return GetDataTab(con, "EXEC sp_pkeys '" + table_name + "'");
        }

        /// <summary>
        /// This method initializes SqlCommand for specified stored procedure (with all the parameters)
        /// </summary>
        public static SqlCommand GetSpParameters(SqlConnection con, SqlTransaction trans, string sp_name) {
            SqlCommand com = new SqlCommand();
            com.Connection = con;
            com.Transaction = trans;
            com.CommandText = sp_name;
            com.CommandType = CommandType.StoredProcedure;
            SqlCommandBuilder.DeriveParameters(com);
            return com;
        }

        /// <summary>
        /// This method fetches list of fields for specified table
        /// </summary>
        public static DataTable GetFieldsOfTable(SqlConnection con, string table_name) {
            return GetDataTab(
                              con, @"
								SELECT
									t.table_schema,
									t.table_name,
									c.column_name,
									c.data_type,
									c.character_maximum_length AS char_len,
									c.numeric_precision AS [precision],
								    c.numeric_scale AS scale,
								    column_default = SUBSTRING(c.column_default, 2, LEN(RTRIM(c.column_default)) - 2),
								    is_nullable = CASE WHEN COLUMNPROPERTY(OBJECT_ID(t .table_schema + '.' + t .table_name), c.column_name, 'AllowsNull') = 1 THEN cast(1 as bit) ELSE cast(0 as bit) END,
								    c.ordinal_position,
									cast(COLUMNPROPERTY(OBJECT_ID(t.table_name), c.column_name, 'IsIdentity') as bit) as is_identity,
                                    cast(COLUMNPROPERTY(OBJECT_ID(t.table_name), c.column_name, 'IsComputed') as bit) as is_computed
								FROM
									information_schema.tables t
									INNER JOIN information_schema.columns C ON t .table_schema = c.table_schema AND t .table_name = c.table_name
								WHERE
									t.table_type = 'BASE TABLE' and c.table_name = '" + table_name + "'");
        }

        /// <summary>
        /// This method fetches list of fields for specified table
        /// </summary>
        public static DataTable GetFieldsOfView(SqlConnection con, string view_name) {
            return GetDataTab(
                              con, @"
                select
	                t.table_schema,
	                t.table_name,
	                c.column_name,
	                c.data_type,
	                c.character_maximum_length AS char_len,
	                c.numeric_precision AS [precision],
                    c.numeric_scale AS scale,
                    column_default = SUBSTRING(c.column_default, 2, LEN(RTRIM(c.column_default)) - 2),
                    is_nullable = CASE WHEN COLUMNPROPERTY(OBJECT_ID(t .table_schema + '.' + t .table_name), c.column_name, 'AllowsNull') = 1 THEN cast(1 as bit) ELSE cast(0 as bit) END,
                    c.ordinal_position,
	                cast(COLUMNPROPERTY(OBJECT_ID(t.table_name), c.column_name, 'IsIdentity') as bit) as is_identity,
                    cast(COLUMNPROPERTY(OBJECT_ID(t.table_name), c.column_name, 'IsComputed') as bit) as is_computed
                from 
                    information_schema.views t
                    INNER JOIN information_schema.columns C ON t .table_schema = c.table_schema AND t .table_name = c.table_name
                where
                    t.table_name = '" + view_name + "'");
        }

        #endregion

        #region xml serialization

        /// <summary>
        /// Serialization
        /// </summary>
        /// <param name="o">object to serialize</param>
        /// <returns>String containing XML serialization</returns>

        public static string SerializeXml(object o) {
            StringBuilder sb = new StringBuilder();
            StringWriter sw = new StringWriter(sb);
            try {
                System.Xml.Serialization.XmlSerializer ser = new System.Xml.Serialization.XmlSerializer(o.GetType());
                ser.Serialize(sw, o);
            } finally {
                sw.Flush();
                sw.Close();
            }
            return sb.ToString();
        }


        /// <summary>
        /// Deserialization.
        /// </summary>
        /// <param name="s">String containing XML serialization</param>
        /// <param name="t">Type to be deserialized</param>
        /// <returns>Deserialized object - has to be typecasted later.</returns>

        public static object DeserializeXml(string s, Type t) {
            StringReader sr = new StringReader(s);
            try {
                System.Xml.Serialization.XmlSerializer ser = new System.Xml.Serialization.XmlSerializer(t);
                return ser.Deserialize(sr);
            } finally { sr.Close(); }
        }

        #endregion
    }
}
