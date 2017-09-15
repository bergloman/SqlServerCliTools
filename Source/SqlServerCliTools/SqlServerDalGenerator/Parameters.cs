using System;
using System.Data.SqlClient;
using System.Xml.Serialization;

namespace DalGenerator {

    /// <summary>
    /// This class contains all code-generator parameters
    /// </summary>
    [XmlRoot(ElementName = "settings", Namespace = "urn:cs-generator", IsNullable = false)]
    public class CodeGenerationParameters {

        /// <summary>Sql server name</summary>
        public string sql_server_name;
        /// <summary>name of the database to create DAL for</summary>
        public string schema_name;

        /// <summary>Should we use username from settings</summary>
        public bool use_username;
        /// <summary>SQL server user</summary>
        public string uid;
        /// <summary>SQL server pwd</summary>
        public string pwd;

        /// <summary>Name of exported file</summary>
        public string export_file_name;

        /// <summary>Base class for generated table wrappers</summary>
        public string base_class_for_tables;

        /// <summary>Prefix that will be given to all classes that represent tables</summary>
        public string class_prefix = "Table";
        /// <summary>Prefix that will be given to all classes that represent views</summary>
        public string view_prefix = "View";
        /// <summary>Prefix for all members that represent table fields</summary>
        public string member_prefix = "";

        /// <summary>If true then stored-procedure wrappers will be created</summary>
        public bool use_sps;
        /// <summary>Name of class that will contain wrapper method for stored procedures</summary>
        public string sp_class_name = "";
        /// <summary>Prefix for stored procedure names</summary>
        public string sp_prefix = "";
        /// <summary>Flag that should be present in stored procedure comment for data-wrapper to be generated. </summary>
        public string sp_data_wrapper_flag = "";
        /// <summary>Flag if data-wrapper should do autotrimming when loading results from stored procedure. </summary>
        public bool sp_data_wrapper_autotrim = false;

        /// <summary>Namespace of generated code</summary>
        public string code_namespace = "Dal";

        /// <summary>list of tables to export - if empty all tables will be exported</summary>
        [XmlElementAttribute("selected_tables")]
        public string[] selected_tables;

        /// <summary>prefix of tables to exclude from export </summary>
        [XmlElementAttribute("excluded_tables_prefix")]
        public string excluded_tables_prefix;

        /// <summary>prefix of tables to exclude from export </summary>
        [XmlElementAttribute("excluded_tables")]
        public string[] excluded_tables;

        /// <summary> Should we skip injection of base classes and interfaces </summary>
        public bool dont_inject_infrastructure_classes = false;
        /// <summary> Should we skip injection of basic data provider for SQL server  </summary>
        public bool dont_inject_basic_sql_provider = false;
        /// <summary> Should we skip injection of IQToolkit-related stuff </summary>
        public bool dont_inject_linq_helpers = true;
        /// <summary> Should query object be created </summary>
        public bool generate_query_object = true;

        /// <summary>If true then some stored procedures return tables - different code will be generated.</summary>
	    public bool use_sp_with_tables = true;
        /// <summary>Prefix of names of stored procedures that return tables.</summary>
		public string prefix_sp_with_tables = "grp_";
        /// <summary>Should field-names be lowercase</summary>
        public bool make_names_lowercase = true;

        /// <summary>Additional namespaces to be used</summary>
        [XmlElementAttribute("additional_namespaces")]
        public string[] additional_namespaces;

        /// <summary>code to be inserted at the beggining of the file</summary>
        public string namespace_start_code;
    }
}

