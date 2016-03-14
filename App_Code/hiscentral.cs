using System;
using System.Diagnostics;
using System.Web;
using System.Collections;
using System.Collections.Generic;
using System.Web.Services;
using System.Web.Services.Protocols;
using System.Data.SqlClient;
using System.Data;
using System.Data.Sql;
using System.Configuration;
using System.Web.Caching;

using com.hp.hpl.jena.rdf.model;
using com.hp.hpl.jena.util.iterator;
using com.hp.hpl.jena.ontology;

using log4net;
using System.Linq;
using System.Xml.Linq;
using System.Xml.XPath;

using System.Net;
using System.Net.Http;
using System.IO;
using System.Globalization;
using System.Text.RegularExpressions;

using SolrNet;
using SolrNet.Attributes;   //.Utils;
using Microsoft.Practices.ServiceLocation;
using SolrNet.Commands.Parameters;


/// <summary>
/// Summary description for hiscentral
/// </summary>

[WebService(Namespace = "http://hiscentral.cuahsi.org/20100205/")]
[WebServiceBinding(ConformsTo = WsiProfiles.BasicProfile1_1)]

public class hiscentral : System.Web.Services.WebService {


    private static readonly ILog log 
	    = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);
    private const string logFormat = "Method:{0}, {1}";

    private static readonly ILog queryLog = LogManager.GetLogger("QueryLog");
    private const string queryLogFormat = "{0}|{1}|{2}|{3}";

private ServiceStatistics _ss = null;
    public ServiceStatistics ServiceStats {
        get {
            if (_ss != null) return _ss;
            //Try pulling from application cache if null
            var ss = Application["ServiceStatistics"];
            if (ss == null)
            {
                _ss = new ServiceStatistics();
                Application.Add("ServiceStatistics", _ss);
            }
            else
            {
                _ss = (ServiceStatistics)ss;
                

            }
            return _ss;
        }
    }

    public hiscentral() {
        //Uncomment the following line if using designed components 
        //InitializeComponent(); 
    }

//     private const string media = "http://www.cuahsi.org/waterquality#medium";
//     private const string mediumPropertyURI = "http://www.cuahsi.org/waterquality#hasMedium";


    public struct Box {
        public double xmin;
        public double xmax;
        public double ymin;
        public double ymax;
    }

    #region sites queries:
    public struct Site {
        public string SiteName;
        public string SiteCode;
        public double Latitude;
        public double Longitude;
        public string HUC;
        public int    HUCnumeric;
        public string servCode;
        public string servURL;
    }

    /*
     * GetSitesInBox2
     * 
     * Input Parameters: 
     * 
     * 	Lat/Long Box, 
     * 	Ontology Concept (optional), 
     * 	// Begin Date (removed/ignored), 
     * 	// End Date (removed/ignored), 
     * 	// Number of Data Values (removed/ignored), 
     * 	A comma separated list of NetworkIDs (Optional)
     * 
     * Returns: A list of all sites that fall within the bounding box, have variables that are mapped 
     * to or fall under the Ontology Concept, overlap the date range of interest, have a minimum number 
     * of data values, and are within the list of services.  
     * Return Format: A list of WaterML siteInfo elements that includes enough information to identify 
     * the service from which the sites were extracted and the HUC Code and HUC Name for the HUC in which 
     * the sites are located (as a general rule, anywhere the siteInfo element is used it should contain  
     * the HUC Code and HUC Name).
     *
     * COUCH: HUC Code and HUC Name are no longer returned. 
     * COUCH: GetSitesInBox and GetSitesInBox2 differ only in input format. 
     */

    [WebMethod]
    public Site[] GetSitesInBox2(
	double xmin, double xmax, double ymin, double ymax, 
	string conceptKeyword, string networkIDs) 
    {

        Box box = new Box();
        box.xmax = xmax;
        box.xmin = xmin;
        box.ymax = ymax;
        box.ymin = ymin;
        int[] ids = new int[0];
        if (networkIDs != "" && networkIDs != " ") {
            String[] sids = networkIDs.Split(',');
            ids = new int[sids.Length];
            for (int i = 0; i < ids.Length; i++) {
                ids[i] = int.Parse(sids[i]);
            }
        }
        return GetSitesInBox(box, conceptKeyword, ids);
    }

   /*
     * GetSitesInBox
     * 
     * Input Parameters: 
     * 
     * 	max latitude, 
     * 	min latitude, 
     * 	max longitude, 
     * 	min longitude,  
     * 	Ontology Concept (optional), 
     * 	// Begin Date (removed/ignored), 
     * 	// End Date (removed/ignored), 
     * 	// Number of Data Values (removed/ignored), 
     * 	A comma separated list of NetworkIDs (Optional)
     * 
     * Returns: A list of all sites that fall within the bounding box, have variables that are mapped 
     * to or fall under the Ontology Concept, and are within the list of services.  
     * Return Format: A list of WaterML siteInfo elements that includes enough information to identify 
     * the service from which the sites were extracted and the HUC Code and HUC Name for the HUC in which 
     * the sites are located (as a general rule, anywhere the siteInfo element is used it should contain  
     * the HUC Code and HUC Name).
     *
     * COUCH: HUC Code and HUC Name are no longer returned.     
     * COUCH: GetSitesInBox and GetSitesInBox2 differ only in input formats.  
     */

    [WebMethod]
    public Site[] GetSitesInBox(Box box, string conceptKeyword, int[] networkIDs) {


        string objecformat = "concept:{0},box({1},{2},{3},{4}),network({5}";
        string methodName = "GetSitesInBox";
        Stopwatch timer = new Stopwatch();
        timer.Start();

        String netString = ""; 
        if (networkIDs != null && networkIDs.Length != 0) {
            for (int i = 0; i < networkIDs.Length; i++) {
                if (i > 0) netString += ",";
                netString += networkIDs[i].ToString();
            }
        }

        log.InfoFormat(logFormat, methodName, "Start", 0,
           String.Format(objecformat,
               conceptKeyword ?? String.Empty,
               box.xmin, box.xmax, box.ymin, box.ymax,
               netString));//Marie - Network String

        string connect = ConfigurationManager.ConnectionStrings["CentralHISConnectionString"].ConnectionString;
        SqlConnection con = new SqlConnection(connect);
        // allow blank keywords through
        Site[] sites = new Site[0];

        String sql = "sp_getSitesInBox";


        using (con) {
            SqlDataAdapter da = new SqlDataAdapter(sql, con);
            da.SelectCommand.CommandTimeout = 300;
            da.SelectCommand.CommandType = CommandType.StoredProcedure;

            da.SelectCommand.Parameters.AddWithValue("@conceptName", conceptKeyword);
            da.SelectCommand.Parameters.AddWithValue("@latmax", box.ymax);
            da.SelectCommand.Parameters.AddWithValue("@latmin", box.ymin);
            da.SelectCommand.Parameters.AddWithValue("@longmax", box.xmax);
            da.SelectCommand.Parameters.AddWithValue("@longmin", box.xmin);
            da.SelectCommand.Parameters.AddWithValue("@networks", netString);
            DataSet ds = new DataSet();
            da.Fill(ds, "SearchCatalog");

            System.Data.DataRowCollection rows = ds.Tables["SearchCatalog"].Rows;
            sites = new Site[rows.Count];
            DataRow row;
            for (int i = 0; i < rows.Count; i++) {
                row = rows[i];
                sites[i] = new Site();
                sites[i].SiteCode = row["SiteCode"] != null ? row["SiteCode"].ToString() : "";
                sites[i].SiteName = row["SiteName"] != null ? row["SiteName"].ToString() : "";
                sites[i].servURL = row["ServiceWSDL"] != null ? row["ServiceWSDL"].ToString() : "";
                sites[i].servCode = row["NetworkName"] != null ? row["NetworkName"].ToString() : "";
                //sites[i].HUCnumeric = row["HUCnumeric"] != null ? (int)row["HUCnumeric"] : 0;
                sites[i].Latitude = (double)row["latitude"];
                sites[i].Longitude = (double)row["longitude"];


            }
        }
        log.InfoFormat(logFormat, methodName, "end", timer.ElapsedMilliseconds,
         String.Format(objecformat,
             conceptKeyword ?? String.Empty,
             box.xmin, box.xmax, box.ymin, box.ymax,
             netString));//marie-networkString
        timer.Stop();
        return sites;
    }

    #endregion

    #region variable queries:
    [WebMethod]
    public MappedVariable[] GetMappedVariables2(String conceptids, String Networkids) {
        String[] ceptsArray = conceptids.Split(',');
        String[] netsArray = Networkids.Split(',');
        return GetMappedVariables(ceptsArray, netsArray);
    }

    [WebMethod]
    public MappedVariable[] GetMappedVariables(String[] conceptids, String[] Networkids) {
        string objecformat = "concept:{0},network({1}";
        string methodName = "GetMappedVariables";
        Stopwatch timer = new Stopwatch();
        timer.Start();

        log.InfoFormat(logFormat, methodName, "Start", 0,
                       String.Format(objecformat,
                       	conceptids == null ? string.Empty : String.Join(",", conceptids),
			Networkids == null ? string.Empty : String.Join(",", Networkids))
        );

	string sql = "sp_getMappedVariables" ; 
	
	// create string of comma-separated concept ids
	string conceptString = ""; 
        if (conceptids != null && conceptids.Length > 0) {
            if (!(conceptids.Length == 1 && conceptids[0].Length == 0)) {
                int i = 0;
                foreach (string cept in conceptids) {
                    if (i > 0) conceptString += ",";
                    i++; conceptString += "'" + cept + "'";
                }
            }
        }

	// create string of comma-separated network ids
        String netString = "";
        if (Networkids != null && Networkids.Length != 0) {
            for (int i = 0; i < Networkids.Length; i++) {
                if (i > 0) netString += ",";
                netString += Networkids[i].ToString();
            }
        }


        string connect = ConfigurationManager.ConnectionStrings["CentralHISConnectionString"].ConnectionString;
        SqlConnection con = new SqlConnection(connect);
        MappedVariable[] mappedVars = new MappedVariable[0];
        using (con) {
            SqlDataAdapter da = new SqlDataAdapter(sql, con);
	        da.SelectCommand.CommandType = CommandType.StoredProcedure; 
            da.SelectCommand.Parameters.AddWithValue("@conceptIds", conceptString);
            da.SelectCommand.Parameters.AddWithValue("@networkIds", netString);

            DataSet ds = new DataSet();
            da.Fill(ds, "MappedVars");

            System.Data.DataRowCollection rows = ds.Tables["MappedVars"].Rows;
            mappedVars = new MappedVariable[rows.Count];
            DataRow row;
            for (int i = 0; i < rows.Count; i++) {
                row = rows[i];
                mappedVars[i] = new MappedVariable();
                mappedVars[i].variableName = row["AltVariableName"] != null ? row["AltVariableName"].ToString() : "";
                mappedVars[i].variableCode = row["AltVariableCode"] != null ? row["AltVariableCode"].ToString() : "";
                mappedVars[i].WSDL = row["ServiceWSDL"] != null ? row["ServiceWSDL"].ToString() : "";
                mappedVars[i].servCode = row["NetworkName"] != null ? row["NetworkName"].ToString() : "";
                mappedVars[i].conceptCode = row["ConceptID"] != null ? row["ConceptID"].ToString() : "";
                //mappedVars[i].conceptKeyword = getOntologyKeyword(mappedVars[i].conceptCode);
            }
        }

        log.InfoFormat(logFormat, methodName, "end", timer.ElapsedMilliseconds,
	    String.Format(objecformat,
		conceptids == null ? string.Empty : String.Join(",", conceptids),
		Networkids == null ? string.Empty : String.Join(",", Networkids))
        );
        timer.Stop();
        return mappedVars;
    }
    // Interface code defines what is passed to aspx page. 
    public struct MappedVariable {
        public string variableName;
        public string variableCode;
        public string servCode;
        public string WSDL;
        public string conceptKeyword;
        public string conceptCode;
    }

    #endregion

    # region ServiceInfo struct and queries
    # region ServiceInfo wiki info
    /* 
     * GetWaterOneFlowServiceInfo 
     * Input Parameters: A comma separated list of ServiceIDs (Optional)
     * Returns: A list of all WaterOneFlow web services registered with HIS Central. 
     * We need a WaterML serviceInfo type to define this and should probably have the following elements.
     * Data Service Name
     * Data Service Title
     * Data Service WSDL URL
     * Data Service Description URL
     * Geographic Extent (xmin, xmax, ymin, ymax)
     * Abstract
     * Contact Name
     * Contact Email
     * Contact Phone
     * Organization
     * Organization Website
     * Supported Methods
     * Citation
     * Value Count
     * Variable Count
     * Site Count
     * Earliest Record DateTime
     * Latest Record DateTime
     * ServiceStatus
     */
    #endregion
    public struct ServiceInfo {
        public string servURL;
        public string Title, ServiceDescriptionURL;
        public string name, Email, phone;
        public string organization, orgwebsite, citation, aabstract;
        public int valuecount;
        public int variablecount, sitecount;
        public int ServiceID;
        public string NetworkName;
        public double minx, miny, maxx, maxy;
        public string serviceStatus;
    }

    [WebMethod]
    public ServiceInfo[] GetServicesInBox(Box box) {
        return GetServicesInBox2(box.xmin, box.ymin, box.xmax, box.ymax);
    }
    [WebMethod]
    public ServiceInfo[] GetServicesInBox2(double xmin, double ymin, double xmax, double ymax) {
        ServiceStats.AddCount("GetServicesInBox2");

        string objecformat = "box({0},{1},{2},{3})";
        string methodName = "GetServicesInBox2";
        Stopwatch timer = new Stopwatch();
        timer.Start();

        queryLog.InfoFormat(queryLogFormat, methodName, "Start", 0,
            String.Format(objecformat, xmin, xmax, ymin, ymax)
	);

//         String sql = "SELECT NetworkID, NetworkName, NetworkTitle, ServiceWSDL, ServiceAbs, ContactName, ContactEmail, ContactPhone, Organization, website,  " +
//                     "   Citation, NetworkVocab, ProjectStatus, Xmin, Xmax, Ymin, Ymax, ValueCount, VariableCount, SiteCount, EarliestRec, LatestRec, ServiceStatus " +
//                     "FROM hisnetworks WITH (NOLOCK) where ispublic='true' " +
//          "AND (((xmin between @minx and @maxx) or (xmax between @minx and @maxx))AND((ymin between @miny and @maxy) or (ymax between @miny and @maxy))) OR " +
//          "(((@minx between xmin and xmax) or (@maxx between xmin and xmax))AND((@miny between ymin and ymax) or (@maxy between ymin and ymax))) ";

	String sql = "sp_getServicesInBox";

        String connect = ConfigurationManager.ConnectionStrings["CentralHISConnectionString"].ConnectionString;

        DataSet ds = new DataSet();
        SqlConnection con = new SqlConnection(connect);
        using (con) {
            SqlDataAdapter da = new SqlDataAdapter(sql, con);
	    da.SelectCommand.CommandType = CommandType.StoredProcedure; 
            da.SelectCommand.Parameters.AddWithValue("@maxy", ymax);
            da.SelectCommand.Parameters.AddWithValue("@miny", ymin);
            da.SelectCommand.Parameters.AddWithValue("@maxx", xmax);
            da.SelectCommand.Parameters.AddWithValue("@minx", xmin);

            log.DebugFormat(logFormat, methodName, da.SelectCommand.CommandText);

            da.Fill(ds, "Service_LIST");
        }
        con.Close();
        //ds.Tables["URL"].Rows
        System.Data.DataRowCollection rows = ds.Tables["Service_LIST"].Rows;

        var r = getServiceInfoArray(rows);
        queryLog.InfoFormat(queryLogFormat, methodName, "end", timer.ElapsedMilliseconds,
	    String.Format(objecformat, xmin, xmax, ymin, ymax)
	);
        timer.Stop();

        return r;

    }
    [WebMethod]
    public ServiceInfo[] GetWaterOneFlowServiceInfo() {
        ServiceStats.AddCount("GetWaterOneFlowServiceInfo");
        string methodName = "GetWaterOneFlowServiceInfo";
        Stopwatch timer = new Stopwatch();
        timer.Start();

        queryLog.InfoFormat(queryLogFormat, methodName, "Start", 0, String.Empty);
        //SELECT     NetworkID, username, NetworkName, NetworkTitle, ServiceWSDL, ServiceAbs, ContactName, ContactEmail, ContactPhone, Organization, website, 
        //                  IsPublic, SupportsAllMethods, Citation, MapIconPath, OrgIconPath, LastHarvested, FrequentUpdates, logo, icon, IsApproved, NetworkVocab, 
        //                  ProjectStatus, CreatedDate, Xmin, Xmax, Ymin, Ymax, ValueCount, VariableCount, SiteCount, EarliestRec, LatestRec, ServiceStatus
        //FROM         HISNetworks
        //WHERE     (IsPublic = 'true')

        String sql = "SELECT NetworkID, NetworkName, NetworkTitle, ServiceWSDL, ServiceAbs, ContactName, ContactEmail, ContactPhone, Organization, website,  " +
                     "   Citation, NetworkVocab, ProjectStatus, Xmin, Xmax, Ymin, Ymax, ValueCount, VariableCount, SiteCount, EarliestRec, LatestRec, ServiceStatus " +
                     "FROM hisnetworks WITH (NOLOCK) where ispublic='true' ";
        String connect = ConfigurationManager.ConnectionStrings["CentralHISConnectionString"].ConnectionString;
        DataSet ds = new DataSet();
        SqlConnection con = new SqlConnection(connect);
        using (con) {
            SqlDataAdapter da = new SqlDataAdapter(sql, con);

            log.DebugFormat(logFormat, methodName, da.SelectCommand.CommandText);

            da.Fill(ds, "Service_LIST");
        }
        con.Close();
        //ds.Tables["URL"].Rows
        System.Data.DataRowCollection rows = ds.Tables["Service_LIST"].Rows;

        var r = getServiceInfoArray(rows);
        queryLog.InfoFormat(queryLogFormat, methodName, "end", timer.ElapsedMilliseconds,
        String.Empty);
        timer.Stop();
        return r;

    }

    private ServiceInfo[] getServiceInfoArray(DataRowCollection rows) {

        ServiceInfo[] infos = new ServiceInfo[rows.Count];
        DataRow row;
        for (int i = 0; i < rows.Count; i++) {
            row = rows[i];
            infos[i] = new ServiceInfo();
            infos[i].ServiceID = int.Parse(row[0].ToString());
            infos[i].servURL = row["ServiceWSDL"] != null ? row["ServiceWSDL"].ToString() : "";
            infos[i].Title = row["NetworkTitle"] != null ? row["NetworkTitle"].ToString() : "";
            infos[i].NetworkName = row["NetworkName"] != null ? row["NetworkName"].ToString() : "";
            if (row["Xmin"] != null && row["Xmin"].ToString() != "") {
                infos[i].minx = double.Parse(row["Xmin"].ToString());
                infos[i].maxx = double.Parse(row["Xmax"].ToString());
                infos[i].miny = double.Parse(row["Ymin"].ToString());
                infos[i].maxy = double.Parse(row["Ymax"].ToString());
            }
            // infos[i].valuecount = (String.IsNullOrEmpty( (string)row["ValueCount"] )) ? Int32.Parse(row["ValueCount"].ToString()) : 0;
            if (row["ValueCount"] != DBNull.Value) {
                try {
                    infos[i].valuecount = Int32.Parse(row["ValueCount"].ToString());
                } catch (OverflowException ex) {
                    infos[i].valuecount = Int32.MaxValue;
                }
            }
            infos[i].variablecount = (row["VariableCount"] != null && row["VariableCount"].ToString() != "") ? int.Parse(row["VariableCount"].ToString()) : 0;
            infos[i].sitecount = (row["SiteCount"] != null && row["SiteCount"].ToString() != "") ? int.Parse(row["SiteCount"].ToString()) : 0;
            infos[i].citation = row["citation"] != null ? row["citation"].ToString() : "";
            infos[i].aabstract = row["ServiceAbs"] != null ? row["ServiceAbs"].ToString() : "";
            infos[i].organization = row["Organization"] != null ? row["Organization"].ToString() : "";
            infos[i].phone = row["ContactPhone"] != null ? row["ContactPhone"].ToString() : "";
            infos[i].Email = row["ContactEmail"] != null ? row["ContactEmail"].ToString() : "";
            infos[i].orgwebsite = row["website"] != null ? row["website"].ToString() : "";
            infos[i].ServiceDescriptionURL = "http://hiscentral.cuahsi.org/pub_network.aspx?n=" + infos[i].ServiceID;
            infos[i].serviceStatus = row["ServiceStatus"] != null ? row["ServiceStatus"].ToString() : "";
        }
        return infos;
    }
    #endregion

    #region Series methods

    public struct SeriesRecord {
        public string ServCode;
        public string ServURL;
        public string location;
        public string VarCode;
        public string VarName;
        public string beginDate;
        public string endDate;
        public string authtoken;
        public int ValueCount;

        public string Sitename;
        public double latitude;
        public double longitude;

        public string datatype;
        public string valuetype;
        public string samplemedium;
        public string timeunits;
        public string conceptKeyword;
        public string genCategory;
        public string TimeSupport;
    }

    //private int[] getNetworkIDS(String[] netnames) { 
    //  //if (netnames == null || netnames.Length=0) return new int[0]; 
    //  //int[]
    //  //String sql = "Select networkid from hisnetworks"
    //}
    //private SeriesRecord[] getSeriesInHuc(string huc, string keyword, 
    //	string beginDate, string endDate, int numRecs, int[] serviceids) { 
    //}
    
    [WebMethod]
    public SeriesRecord[] GetSeriesCatalogForBox(Box box, String conceptCode, 
		    int[] networkIDs, string beginDate, string endDate) 
    {
        ServiceStats.AddCount("GetSeriesCatalogForBox");
        String networkString = "";
        if (networkIDs != null && networkIDs.Length > 0) {
            for (int i = 0; i < networkIDs.Length; i++) {
                if (i > 0) networkString += ",";
                networkString += (networkIDs[i]).ToString();
            }
        }
        return GetSeriesCatalogForBox2(box.xmin, box.xmax, box.ymin, box.ymax, conceptCode, networkString, beginDate, endDate);
    }

    [WebMethod]
    public SeriesRecord[] GetSeriesCatalogForBox2(double xmin, double xmax, double ymin, double ymax,
                              string conceptKeyword, String networkIDs,
                          string beginDate, string endDate)
    {
        // int.MaxValue; outofMemory for 60,000
        int nrows = int.Parse(System.Configuration.ConfigurationManager.AppSettings["SOLRnrows"]);

        string endpoint = System.Configuration.ConfigurationManager.AppSettings["SOLRendpoint"];
        if (!endpoint.EndsWith("/")) endpoint = endpoint + "/";

        //call requestUrl() to get the url to Solr
        string url = endpoint + 
                        requestUrl(xmin, xmax, ymin, ymax,
                                conceptKeyword, networkIDs,
                                beginDate, endDate, nrows);

        XDocument xDocument;
        SeriesRecord[] series = null;
        string response = null;
        using (WebClient client = new WebClient())
        {
            response = client.DownloadString(url);
            TextReader xmlReader = new StringReader(response);
            xDocument = XDocument.Load(xmlReader);

            //If using .Net 4.0 or above, better to use Linq to XML
            // Note: the following fields could be NULL
            //       SiteName, DataType, SampleMedium, TimeUnits, GeneralCategory
            series =
            (from o in xDocument.Descendants("doc")
                //let eleStr = o.Elements("str")
                select new SeriesRecord()
                {
                    location = o.Elements("str").Single(x => x.Attribute("name").Value == "SiteCode").Value.ToString(), //???
                    //SiteCode like 'EPA:SDWRAP:LOUCOTTMC01',  Sitename==NULL
                    Sitename = o.Descendants("str").Where(e => (string)e.Attribute("name") == "SiteName").Count() == 0 ? "" : o.Elements("str").Single(x => x.Attribute("name").Value == "SiteName").Value.ToString(),
                    ServURL = o.Elements("str").Single(x => x.Attribute("name").Value == "ServiceWSDL").Value.ToString(),
                    ServCode = o.Elements("str").Single(x => x.Attribute("name").Value == "NetworkName").Value.ToString(),
                    latitude = double.Parse(o.Elements("double").Single(x => x.Attribute("name").Value == "Latitude").Value.ToString()),
                    longitude = double.Parse(o.Elements("double").Single(x => x.Attribute("name").Value == "Longitude").Value.ToString()),
                    ValueCount = int.Parse(o.Elements("long").Single(x => x.Attribute("name").Value == "Valuecount").Value.ToString()),
                    VarName = o.Elements("str").Single(x => x.Attribute("name").Value == "VariableName").Value.ToString(),
                    VarCode = o.Elements("str").Single(x => x.Attribute("name").Value == "VariableCode").Value.ToString(),
                    beginDate = o.Elements("date").Single(x => x.Attribute("name").Value == "BeginDateTime").Value.ToString(),
                    endDate = o.Elements("date").Single(x => x.Attribute("name").Value == "EndDateTime").Value.ToString(),
                    datatype = o.Descendants("str").Where(e => (string)e.Attribute("name") == "DataType").Count() == 0 ? "" : o.Elements("str").Single(x => x.Attribute("name").Value == "DataType").Value.ToString(),
                    valuetype = o.Elements("str").Single(x => x.Attribute("name").Value == "ValueType").Value.ToString(),
                    samplemedium = o.Descendants("str").Where(e => (string)e.Attribute("name") == "SampleMedium").Count() == 0 ? "" : o.Elements("str").Single(x => x.Attribute("name").Value == "SampleMedium").Value.ToString(),
                    timeunits = o.Descendants("str").Where(e => (string)e.Attribute("name") == "TimeUnits").Count() == 0 ? "" : o.Elements("str").Single(x => x.Attribute("name").Value == "TimeUnits").Value.ToString(),
                    conceptKeyword = o.Elements("str").Single(x => x.Attribute("name").Value == "ConceptKeyword").Value.ToString(),
                    genCategory = o.Descendants("str").Where(e => (string)e.Attribute("name") == "GeneralCategory").Count() == 0 ? "" : o.Elements("str").Single(x => x.Attribute("name").Value == "GeneralCategory").Value.ToString(),
                    TimeSupport = o.Elements("long").Single(x => x.Attribute("name").Value == "TimeSupport").Value.ToString(),
                }).ToArray();          
        }

        return series;
    }

    //added by Yaping, Dec.2015
    public void logTimer(string logFile, string text)
    {
        System.IO.TextWriter tw = new System.IO.StreamWriter(logFile, true);
        text = System.DateTime.Now.ToString() + "||" + text ;
        tw.WriteLine(text);
        System.Console.WriteLine(text);
        Console.WriteLine();
        Console.WriteLine();
        tw.Close();
        return;
    }

    //added by Yaping, Dec.2015
    public string requestUrl(double xmin, double xmax, double ymin, double ymax,
                              string conceptKeyword, string networkIDs,
                          string beginDate, string endDate, int nrows)
    {
        string parameters;
        string beginDate2, endDate2;
        string qNetworkIDs;
        string qConcept;
        string qLat, qLon;
                          
        if (networkIDs.Equals(""))
        {
            qNetworkIDs = @"NetworkID:*";
        }
        else if (networkIDs.Length == 1)
        {
            qNetworkIDs = String.Format("NetworkID:{0}", networkIDs);
        }
        else
        {
            //for multiple networkIDs, select?q=NetworkID:(1 2 3)
            string[] parts = networkParser(networkIDs);
            qNetworkIDs = @"NetworkID:(";
            foreach (string part in parts)
            {
                qNetworkIDs += part + ' ';
            }
            qNetworkIDs += ')';
        }

        //phase query, i.e., multiple terms insequence, "Discharge, stream"
        //select?q=NetworkID:1+AND+ConceptKeyword:%22Discharge, stream%22
        qConcept = (conceptKeyword.Equals("") || conceptKeyword.Equals("all", StringComparison.InvariantCultureIgnoreCase)) ?
            @"ConceptKeyword:*" : String.Format("ConceptKeyword:%22{0}%22", conceptKeyword);

        qLat = String.Format("Latitude:[{0:0.0000} {1:0.0000}]", ymin, ymax);
        qLon = String.Format("Longitude:[{0:0.0000} {1:0.0000}]", xmin, xmax);

        beginDate2 = DateTime.ParseExact(beginDate, "MM/dd/yyyy", CultureInfo.InvariantCulture).ToString("yyyy-MM-dd");
        endDate2 = DateTime.ParseExact(endDate, "MM/dd/yyyy", CultureInfo.InvariantCulture).ToString("yyyy-MM-dd");
        var qBeginDT = String.Format(@"BeginDateTime:[* TO {0}T00:00:00Z]", endDate2);
        var qEndDT = String.Format(@"EndDateTime:[{0}T00:00:00Z TO *]", beginDate2);

        //query parameters to solr
        parameters = String.Format(@"select?defType=edismax&q=*:*&fq={0}&fq={1}&fq={2}&fq={3}&fq={4}&fq={5}&rows={6}",
                qNetworkIDs, qConcept, qLat, qLon, qBeginDT, qEndDT, nrows);
        
        return parameters;
    }

    //added by Yaping, Sep.2015
    //input: 1, 2-5, 8; 10..12
    //able to rmove duplicate networkIDs, e.g., 2, 1-4  =? 1, 2, 3, 4
    private string[] networkParser(String s)
    {
        char[] delimiters = new char[] { ',', ';', ' ' };
        String[] parts = s.Split(delimiters, StringSplitOptions.RemoveEmptyEntries);
        HashSet<string> networkSet = new HashSet<string>();
        //Regex reRange = new Regex(@"^\s*((?<from>\d+)|(?<from>\d+)(?<sep>(-|\.\.))(?<to>\d+)|(?<sep>(-|\.\.))(?<to>\d+)|(?<from>\d+)(?<sep>(-|\.\.)))\s*$");
        Regex reRange = new Regex(@"^\s*((?<from>\d+)|(?<from>\d+)(?<sep>(-|\.\.))(?<to>\d+))\s*$");
        foreach (String part in parts)
        {
            Match maRange = reRange.Match(part);
            if (maRange.Success)
            {
                Group gFrom = maRange.Groups["from"];
                Group gTo = maRange.Groups["to"];
                Group gSep = maRange.Groups["sep"];

                if (gSep.Success)
                {
                    Int32 from = -1;
                    Int32 to = -1;
                    if (gFrom.Success)
                        from = Int32.Parse(gFrom.Value);
                    if (gTo.Success)
                        to = Int32.Parse(gTo.Value);
                    for (Int32 page = from; page <= to; page++)
                        networkSet.Add(page.ToString());
                }
                else if (gFrom.Success)
                    networkSet.Add(Int32.Parse(gFrom.Value).ToString());
                else
                    throw new InvalidOperationException("Input NetworkID string is invalid!");
            }
        }
        return networkSet.ToArray();
    }

    //added by Yaping, Dec.2015
    public static void Write2LogFile(string message, string outputFile)
    {
        string line = DateTime.Now.ToString() + " | ";
        line += message;

        FileStream fs = new FileStream(outputFile, FileMode.Append, FileAccess.Write, FileShare.None);
        StreamWriter writer = new StreamWriter(fs);
        writer.WriteLine(line);
        writer.Flush();
        writer.Close();
    }

    [WebMethod]
    public SeriesRecord[] getSeriesCatalogInBoxPaged(
	double xmin, double xmax, double ymin, double ymax, 
	string conceptKeyword, String networkIDs, 
	string beginDate, string endDate, int pageno) {
        ServiceStats.AddCount("getSeriesCatalogInBoxPaged");

        string objecformat = "concept:{0},box({1},{2},{3},{4}),network({5},daterange{6}-{7}";
        string methodName = "getSeriesCatalogInBoxPaged";

        Stopwatch timer = new Stopwatch();
        timer.Start();
        string networksString = networkIDs ?? String.Empty;

        queryLog.InfoFormat(queryLogFormat, methodName, "Start", 0,
           String.Format(objecformat,
           	conceptKeyword ?? String.Empty,
           	xmin, xmax, ymin, ymax,
           	networksString,
           	beginDate ?? String.Empty, endDate ?? String.Empty)
           );

        string connect = ConfigurationManager.ConnectionStrings["CentralHISConnectionString"].ConnectionString;
        SqlConnection con = new SqlConnection(connect);

        SeriesRecord[] series = new SeriesRecord[0];

        bool filterNetwork = false;
        bool filterKeyword = false;


        using (con) {


            SqlCommand cmd = new SqlCommand("sp_SeriesSearch", con);

            cmd.CommandType = CommandType.StoredProcedure;
            cmd.Parameters.AddWithValue("@minx", xmin);
            cmd.Parameters.AddWithValue("@miny", ymin);
            cmd.Parameters.AddWithValue("@maxx", xmax);
            cmd.Parameters.AddWithValue("@maxy", ymax);
            cmd.Parameters.AddWithValue("@beginDate", beginDate);
            cmd.Parameters.AddWithValue("@endDate", endDate);
            cmd.Parameters.AddWithValue("@pageno", pageno);
            cmd.CommandTimeout = 600;

            if (networkIDs != null && networkIDs != "") {
                cmd.Parameters.AddWithValue("@networkIDs", networkIDs);
                filterNetwork = true;
            }
            if (conceptKeyword != null && conceptKeyword != "") {
                //verify Keyword is valid, and replace synonyms
                conceptKeyword = ResolveSynonyms(conceptKeyword);
                if (conceptKeyword == "") {
                    throw (new Exception("concept keyword not found"));
                }
                cmd.Parameters.AddWithValue("@conceptName", conceptKeyword);
                filterKeyword = true;
            }
            if (filterNetwork && filterKeyword) cmd.CommandText = "sp_SeriesSearch_keyword_NetworkIDs";
            if (filterKeyword && !filterNetwork) cmd.CommandText = "sp_SeriesSearch_keyword";
            if (filterNetwork && !filterKeyword) cmd.CommandText = "sp_SeriesSearch_NetworkIDs";

            SqlDataAdapter da = new SqlDataAdapter(cmd);

            DataSet ds = new DataSet();

            da.Fill(ds, "SearchCatalog");

            System.Data.DataRowCollection rows = ds.Tables["SearchCatalog"].Rows;
            series = new SeriesRecord[rows.Count];
            DataRow row;
            for (int i = 0; i < rows.Count; i++) {
                row = rows[i];
                series[i] = new SeriesRecord();
                series[i].location = row[0] != null ? row[0].ToString() : "";
                series[i].Sitename = row[1] != null ? row[1].ToString() : "";
                series[i].ServURL = row["ServiceWSDL"] != null ? row["ServiceWSDL"].ToString() : "";
                series[i].ServCode = row["NetworkName"] != null ? row["NetworkName"].ToString() : "";
                series[i].latitude = (double)row["latitude"];
                series[i].longitude = (double)row["longitude"];
                series[i].ValueCount = (int)row["ValueCount"];
                series[i].VarName = row["VariableName"] != null ? row["VariableName"].ToString() : "";
                series[i].VarCode = row["VariableCode"] != null ? row["VariableCode"].ToString() : "";
                series[i].beginDate = row["BeginDateTime"] != null ? row["BeginDateTime"].ToString() : "";
                series[i].endDate = row["EndDateTime"] != null ? row["EndDateTime"].ToString() : "";
                /*  datatype; valuetype;samplemedium;timeunits; conceptKeyword; genCategory;*/
                series[i].datatype = row["DataType"] != null ? row["DataType"].ToString() : "";
                series[i].valuetype = row["ValueType"] != null ? row["ValueType"].ToString() : "";
                series[i].samplemedium = row["SampleMedium"] != null ? row["SampleMedium"].ToString() : "";
                series[i].timeunits = row["TimeUnits"] != null ? row["TimeUnits"].ToString() : "";
                series[i].conceptKeyword = row["conceptKeyword"] != null ? row["conceptKeyword"].ToString() : "";
                series[i].genCategory = row["GeneralCategory"] != null ? row["GeneralCategory"].ToString() : "";
                series[i].TimeSupport = row["TimeSupport"] != null ? row["TimeSupport"].ToString() : "";

            }
        }

        queryLog.InfoFormat(queryLogFormat, methodName, "end", timer.ElapsedMilliseconds,
         	String.Format(objecformat,
		 	conceptKeyword ?? String.Empty,
		 	xmin, xmax, ymin, ymax,
		 	networksString,
		 	beginDate ?? String.Empty, endDate ?? String.Empty
		)
        );
        timer.Stop();
        return series;
    }
    #endregion

    # region ontology stuff

    public struct OntologyConcpt {
        string ConceptID;
        string ConceptText;
    }
    public struct OntologyPath {
        public string conceptID;
        public string SearchableKeyword;
        public string ConceptName;
        public string ConceptPath;
    }

    [WebMethod]
    public OntologyPath[] getSearchablePaths() {
        ServiceStats.AddCount("getSearchablePaths");

        const string methodName = "getSearchablePaths";
        String sql = "SELECT ConceptID,synonym as SearchableConcept,ConceptName,Path FROM v_SynonymLookup order by path";

        OntologyPath[] thetable = new OntologyPath[0];

        OntologyPath item;

        DataSet ds = new DataSet();
        String connect = ConfigurationManager.ConnectionStrings["CentralHISConnectionString"].ConnectionString;
        SqlConnection con = new SqlConnection(connect);
        int i = 0;
        using (con) {


            SqlDataAdapter da = new SqlDataAdapter(sql, con);

            log.DebugFormat(logFormat, methodName, da.SelectCommand.CommandText);

            da.Fill(ds, "rows");
            thetable = new OntologyPath[ds.Tables["rows"].Rows.Count];

            foreach (DataRow dataRow in ds.Tables["rows"].Rows) {
                item = new OntologyPath();
                item.conceptID = dataRow[0].ToString();
                item.SearchableKeyword = dataRow[1].ToString();
                item.ConceptName = dataRow[2].ToString();
                item.ConceptPath = dataRow[3].ToString();

                thetable[i] = item;

                i++;
            }

        }
        return thetable;
    }


    /*
     * Canonicalize a synonym as the concept name to which it refers. 
     * 	Inputs: Synonym or concept name
     * 	Outputs: Concept name to which synonym refers
     * 
     * This routine has the unfortunate behavior that 
     * if a keyword is both a synonym and a concept, 
     * the synonym wins over the concept. 
     * This should be changed to use real concepts 
     * even if a synonym has been defined in error.  
     */

    private string ResolveSynonyms(String keyword) {

        string returnval = "";
        DataSet ds = new DataSet();
        String connect = ConfigurationManager.ConnectionStrings["CentralHISConnectionString"].ConnectionString;
        SqlConnection con = new SqlConnection(connect);
        int i = 0;
        string sql = "select conceptName from v_synonymlookup where synonym = @conceptName";
        using (con) {

            SqlDataAdapter da = new SqlDataAdapter(sql, con);
            da.SelectCommand.Parameters.Add("@conceptName", keyword);
            da.Fill(ds, "conceptlist");
        }
        if (ds.Tables["conceptlist"].Rows.Count >= 1) {
            DataRow dataRow = ds.Tables["conceptlist"].Rows[0];
            returnval = dataRow[0].ToString();
        }
        return returnval;
    }
    #region removed methods
    
    //[WebMethod]
    //public OntologyConcept[] GetSearchableConcepts() {
    //  WebRequest objWebClient = System.Net.HttpWebRequest.Create(url);
    //  WebResponse objResponse;
    //
    //  objResponse = objWebClient.GetResponse();
    //
    //  String strResult;
    //
    //  using (StreamReader sr =
    //      new StreamReader(objResponse.GetResponseStream()))
    //  {
    //    strResult = sr.ReadToEnd();
    //  }
    //
    //  Response.Write(strResult);
    //}
    //// need to come back to optimize these searches..
    //[WebMethod]
    //public String getOntologyKeyword(String conceptCode) { 
    //   OntologyClass[] ont = GetSearchableConcepts();
    //   OntologyClass o;
    //   for (int i=0;i<ont.Length;i++){
    //       o=ont[i];
    //       if (o.conceptcode.Equals(conceptCode)) return o.keyword;
    //   }
    //   return "ConceptCode not found";     
    //
    //}
    //[WebMethod]
    //public String getOntologyConceptCode(String keyword) { 
    //     OntologyClass[] ont = GetSearchableConcepts();
    //   OntologyClass o;
    //   for (int i=0;i<ont.Length;i++){
    //       o=ont[i];
    //       if (o.keyword.Equals(keyword)) return o.conceptcode;
    //   }
    //   return "ConceptCode not found"; 
    //}
    #endregion

    /* 
     * getOntologyKeywords
     * 	Input: none.
     * 	Output: all available keywords as strings. 
     *
     * Return a memcached list of ontology keywords with a cache timeout of three days. 
     * This means that ontology updates will not be seen for three days by HydroDesktop
     * COUCH: Changing this to one hour 5/29/2014
     */

    public String[] getOntologyKeywords() {

        // allowing blank keywords through
	// COUCH 2014/05/30: no more blank keywords exist
        String sql = "SELECT conceptName from v_searchableConcepts";
        //  appContext = HttpContext.Current;
        string cacheKey = "ajaxVocabulary";

	// deserialize a string cached version of the item
        string[] autoCompleteWordList = (string[])HttpRuntime.Cache.Get(cacheKey);
        if (autoCompleteWordList == null) {
            DataSet ds = new DataSet();
            String connect = ConfigurationManager.ConnectionStrings["CentralHISConnectionString"].ConnectionString;
            SqlConnection con = new SqlConnection(connect);
            int i = 0;
            using (con) {
                SqlDataAdapter da = new SqlDataAdapter(sql, con);
                da.Fill(ds, "conceptlist");
                autoCompleteWordList = new String[ds.Tables["conceptlist"].Rows.Count];

                foreach (DataRow dataRow in ds.Tables["conceptlist"].Rows) {

                    //conceptid = dataRow["conceptid"].ToString();
                    String cptcode = dataRow["conceptName"].ToString();
                    //conceptKeyword = dataRow["conceptKeyword"].ToString();
                    autoCompleteWordList[i] = cptcode;
                    i++;
                }
            }
            //autoCompleteWordList = words.ToArray();
            Array.Sort(autoCompleteWordList, new CaseInsensitiveComparer());
	    // this includes an implicit serialization autoCompleteWordList.toString()
            HttpRuntime.Cache.Add(cacheKey, autoCompleteWordList, null, 
		DateTime.Now.AddHours(1), Cache.NoSlidingExpiration, CacheItemPriority.High, null);
        }
        return autoCompleteWordList;
        //  autoCompleteWordList = temp;
        //  appContext.Cache.Insert("ajaxVocabulary", autoCompleteWordList);

    }

//     /*
//      * COUCH: Obsoleted by code revision 2014/05/30
//      * Get all concept paths associated with a concept
//      * This routine is BROKEN. It always returns an empty array
//      * Thus there is strong evidence that it is unused. 
//      */
// 
//     public String[] GetConceptPaths() {
// 
//         int[] concepts = new int[0];
//         int id;
//         
// 	// COUCH:  if the ConceptName in ConceptPaths does not agree with the 
// 	// ConceptName in Concepts, the name in Concepts "wins".
// 	 
// //      String sql = "SELECT  ConceptPaths.ConceptID, ConceptPaths.Path, ConceptPaths.ConceptKeyword " +
// //                   " FROM  ConceptPaths WITH (NOLOCK) INNER JOIN " +
// //                   " v_searchableConcepts ON ConceptPaths.ConceptID = v_searchableConcepts.ConceptID" +
// //                   " WHERE     (v_searchableConcepts.ConceptName = @conceptName)";
// 
// 	String sql = "sp_getConceptPaths"; 
//         DataSet ds = new DataSet();
//         String connect = ConfigurationManager.ConnectionStrings["CentralHISConnectionString"].ConnectionString;
//         SqlConnection con = new SqlConnection(connect);
// 
//         using (con) {
//             SqlDataAdapter da = new SqlDataAdapter(sql, con);
// 	    da.SelectCommand.CommandType=CommandType.StoredProcedure; 
//             
//             da.Fill(ds, "conceptPath");
// 
//             //con.Close();
//             int rowcount = ds.Tables["conceptPath"].Rows.Count;
//             if (rowcount > 0) {
//                 String path = ds.Tables["conceptPath"].Rows[0][1].ToString();
//                 id = (int)ds.Tables["conceptPath"].Rows[0][0];
//                 concepts = new int[1];
//                 concepts[0] = id;
//                 sql = "Select conceptid, path from conceptPaths WITH (NOLOCK) where path like '" + path + "%'";
//                 da = new SqlDataAdapter(sql, con);
//                 da.Fill(ds, "conceptids");
//                 int i = 0;
//                 rowcount = ds.Tables["conceptids"].Rows.Count;
//                 if (rowcount > 0) {
//                     concepts = new int[rowcount];
//                     foreach (DataRow dataRow in ds.Tables["conceptids"].Rows) {
//                         concepts[i] = (int)dataRow[0];
//                         i++;
//                     }
//                 }
//             }
//         }
//         con.Close();
//         return new String[0]; // ERROR: returns an empty path at all times 
//     }

    /* 
     * Return a list of Searchable Keywords for use in autocompletion in HydroDesktop
     * Use a cached version of the ontology with a timeout of one hour. 
     */
    [WebMethod]
    public String[] GetSearchableConcepts() {
        ServiceStats.AddCount("GetSearchableConcepts");
        return getOntologyKeywords();
    }

    [WebMethod]
    public OntologyNode getOntologyTree(String conceptKeyword) {
        ServiceStats.AddCount("getOntologyTree");

        if (conceptKeyword == null || conceptKeyword.Equals("")) conceptKeyword = "Hydrosphere";
        String sql = "SELECT conceptid, conceptName from v_ConceptsUnionSynonyms where conceptName  = @conceptKeyword";
        DataSet ds = new DataSet();
        String connect = ConfigurationManager.ConnectionStrings["CentralHISConnectionString"].ConnectionString;
        SqlConnection con = new SqlConnection(connect);

        using (con) {

            SqlDataAdapter da = new SqlDataAdapter(sql, con);
            da.SelectCommand.Parameters.AddWithValue("conceptKeyword", conceptKeyword);
            da.Fill(ds, "concept");
        }
        con.Close();

        int rowcount = ds.Tables["concept"].Rows.Count;
        OntologyNode node = new OntologyNode();
        if (rowcount > 0) {
            DataRow row = ds.Tables["concept"].Rows[0];

            node.keyword = row[1].ToString();
            node.conceptid = (int)row[0];
            return getChildNodes(node);
        }
        return node;

    }

// COUCH: Obsoleted by code revision 2014/05/29
//    private string getCommaString(String[] ss) {
//        System.Text.StringBuilder sb = new System.Text.StringBuilder();
//        for (int i = 0; i < ss.Length; i++) {
//            if (i > 0) sb.Append(',');
//            sb.Append("'").Append(ss[i]).Append("'");
//        }
//        return sb.ToString();
//    }
//    private string getCommaInt(int[] ss) {
//        System.Text.StringBuilder sb = new System.Text.StringBuilder();
//        for (int i = 0; i < ss.Length; i++) {
//            if (i > 0) sb.Append(',');
//            sb.Append(ss[i]);
//        }
//        return sb.ToString();
//    }

//     /*
//      * COUCH: It is likely that this routine is not used anywhere anymore
//      * COUCH: The uses in this file have been replaced with other code. 
//      * getChildIDsFlat
//      * 	Input: a concept name
//      * 	Output: an array of the concept IDs related to that concept name by hierarchy
//      *
//      * COUCH: 5/29/2014 This routine returns a list of child IDS and is the routine that 
//      * exhibited the Barium bug. 
//      *
//      * To address the bug, it has been modified to consistently return the ID of the root concept as well 
//      * as the IDs of children. The reason that it did not return the root was due to an assumption that 
//      * variables are underneath concepts in the ontology, which is false (and has been for some time). 
//      * 
//      */
// 
//     public int[] getChildIDsFlat(String conceptName) {
//         int[] concepts = new int[0];
//         int id;
//  
// 	   String sql = "SELECT ConceptID from FN_getChildIDs(@conceptName)"; 
//         SqlCommand cmd = new SqlCommand(sql);
//         cmd.Parameters.AddWithValue("@conceptName", conceptName);
//         cmd.CommandTimeout = 300;
//         DataSet ds = new DataSet();
//         String connect = ConfigurationManager.ConnectionStrings["CentralHISConnectionString"].ConnectionString;
//         SqlConnection con = new SqlConnection(connect);
// 
//         using (con) {
//             SqlDataAdapter da = new SqlDataAdapter(sql, con);
//             da.SelectCommand.Parameters.AddWithValue("conceptName", conceptName);
//             da.Fill(ds, "conceptPath");
//             int rowcount = ds.Tables["conceptPath"].Rows.Count;
//             if (rowcount > 0) {
// 		concepts = new int[rowcount]; 
// 		for (int i=0; i<rowcount; i++) 
// 		    concepts[i] = Rows[i][0]; 
// 	    }
//         }
//         con.Close();
//         return concepts;
//     }
// 
//     /*
//      * COUCH: It is likely that this routine is not used anywhere anymore
//      * COUCH: The uses in this file have been replaced with other code. 
//      */
//
//     public String[] getChildConceptsFlat(String conceptName) {
//         String[] concepts = new String[1];
//         concepts[0] = conceptName;
// 
// 	   String sql = "SELECT ConceptID from FN_getChildConcepts(@conceptName)"; 
//         SqlCommand cmd = new SqlCommand(sql);
//         cmd.Parameters.AddWithValue("@conceptName", conceptName);
//         cmd.CommandTimeout = 300;
//         DataSet ds = new DataSet();
//         String connect = ConfigurationManager.ConnectionStrings["CentralHISConnectionString"].ConnectionString;
//         SqlConnection con = new SqlConnection(connect);
// 
//         using (con) {
//             SqlDataAdapter da = new SqlDataAdapter(sql, con);
//             da.SelectCommand.Parameters.AddWithValue("conceptName", conceptName);
//             da.Fill(ds, "conceptPath");
//             int rowcount = ds.Tables["conceptPath"].Rows.Count;
//             if (rowcount > 0) {
// 		concepts = new String[rowcount]; 
// 		for (int i=0; i<rowcount; i++) 
// 		    concepts[i] = Rows[i][0]; 
// 	    }
//         }
//         con.Close();
//         return concepts;
//     }

    private OntologyNode getChildNodes(OntologyNode parentNode) {
        OntologyNode node = new OntologyNode();
        String sql = "SELECT conceptid,  conceptName from v_conceptHierarchy where parentid = " + parentNode.conceptid + ";";

        DataSet ds = new DataSet();
        String connect = ConfigurationManager.ConnectionStrings["CentralHISConnectionString"].ConnectionString;
        SqlConnection con = new SqlConnection(connect);

        using (con) {
            SqlDataAdapter da2 = new SqlDataAdapter(sql, con);
            da2.Fill(ds, "concepts");
        }
        con.Close();


        //should be only one
        String conceptKeyword;
        int conceptid;
        int i = 0;

        int rowcount = ds.Tables["concepts"].Rows.Count;
        if (rowcount > 0) {
            OntologyNode[] child = new OntologyNode[rowcount];
            foreach (DataRow dataRow in ds.Tables["concepts"].Rows) {

                conceptid = (int)dataRow["conceptid"];
                //conceptcode = dataRow["conceptCode"].ToString();
                conceptKeyword = dataRow["conceptName"].ToString();
                child[i] = new OntologyNode();
                child[i].keyword = conceptKeyword;
                child[i].conceptid = conceptid;
                //rentNode.ChildNodes.Add(childNode);
                child[i] = getChildNodes(child[i]);
                //nextIDs.Add(conceptid);
                //conceptcode = dataRow["conceptCode"].ToString();
                i++;
            }
            parentNode.childNodes = child;
        }
        return parentNode;
    }

    public struct OntologyNode {
        public string keyword;
        public int conceptid;
        public OntologyNode[] childNodes;
    }
    /* 
     * This prefix search routine provides a word match list for HD. 
     * It is not documented in the main API returns for the catalog. 
     */
    [WebMethod]
    public string[] GetWordList(string prefixText, int count) {
        ServiceStats.AddCount("GetWordList");

        List<String> wordlist = new List<String>();

        int i = 0;
        string connect = ConfigurationManager.ConnectionStrings["CentralHISConnectionString"].ConnectionString;
        SqlConnection con = new SqlConnection(connect);
        // String sql1 = "SELECT conceptName from v_searchableconcepts where conceptName = @prefixText order by conceptName";
        String sql = "SELECT ConceptName from FN_getWordList(@prefixText,@count)";
        DataSet ds = new DataSet();

        using (con) {
            // get the items that match prefix at the beginning of the text or in the middle. 
            SqlDataAdapter da = new SqlDataAdapter(sql, con);
            da.SelectCommand.Parameters.AddWithValue("prefixText", prefixText);
	        da.SelectCommand.Parameters.AddWithValue("count", count); 
            da.Fill(ds, "words");

            foreach (DataRow dataRow in ds.Tables["words"].Rows) {
                if (!wordlist.Contains(dataRow[0].ToString())) {
                    wordlist.Add(dataRow[0].ToString());
                    i++;
                }
                if (i >= count) return wordlist.ToArray();
            }
        }
        con.Close();
        return wordlist.ToArray();
    }

    # region commented out stuff

    //public class OntologyClass
    //{
    //    public string keyword;
    //    public string conceptcode;
    //}

    //private List<string> analyzeNodes(OntModel om)
    //{
    //  List<string> elements = new List<string>();
    //  List<string> mediumList = new List<string>();
    //  mediumList = getMediumList(om);
    //  for (ExtendedIterator allClassesItr = om.listClasses(); allClassesItr
    //      .hasNext(); )
    //  {
    //    OntClass k = (OntClass)allClassesItr.next();

    //    if (isCorrectElement(k))
    //    {
    //      elements.Add(k.getLabel("en"));
    //      if (checkMediumRestriction(om, k))
    //      {

    //        if (mediumList != null)
    //        {
    //          foreach (String ml in mediumList)
    //          {
    //            elements.Add(k.getLabel("en") + " (" + ml + ")");
    //          }
    //        }
    //      }
    //    }
    //  }


    //  return elements;
    //}

    //private bool isCorrectElement(OntClass c)
    //{
    //  bool iscor = false;
    //  //Make sure to handle exceptions!! 
    //  if (c != null)
    //  {
    //    if (c.getLocalName() != null && c.getNameSpace() != null )
    //    {
    //      iscor = c.getNameSpace().IndexOf("extended") < 0 && c.getNameSpace().IndexOf("navigation") < 0 && c.getNameSpace().IndexOf("gcmd") < 0 && !c.getLocalName().Equals("medium") && c.getLocalName().IndexOf("Axiom_") < 0 && (c.getLabel("en").ToUpper() != "OTHER");
    //    }

    //  }

    //  return iscor;
    //}
    //private bool checkMediumRestriction(OntModel m, OntClass k)
    //{
    //  bool hasMediumOptions = false;

    //  for (ExtendedIterator supClassesItr = k.listSuperClasses(); supClassesItr.hasNext(); )
    //  {
    //    OntClass s = (OntClass)supClassesItr.next();
    //    if (s.isRestriction())
    //    {

    //      Restriction r = s.asRestriction();
    //      if (r.isAllValuesFromRestriction())
    //      {

    //        AllValuesFromRestriction h = r.asAllValuesFromRestriction();
    //        if (h.getOnProperty().getLocalName() == "hasMedium")
    //        {
    //          hasMediumOptions = true;
    //        }

    //      }
    //    }

    //  }

    //  return hasMediumOptions;
    //}
    //private List<string> getMediumList(OntModel om)
    //{

    //  // Load the medium property to be used later
    //  //mediumProperty = om.getDatatypeProperty(mediumPropertyURI);

    //  // Here's the class where medium CV is located
    //  OntClass medium = om.getOntClass(media);

    //  // The List to store allowable media
    //  List<string> mediumList = new List<string>();
    //  if (medium != null)
    //  {
    //    // Get the medium instances
    //    for (ExtendedIterator mediumInst = medium.listInstances(); mediumInst.hasNext(); )
    //    {
    //      Individual i = (Individual)mediumInst.next();
    //      mediumList.Add(i.getLabel("en"));

    //    }
    //  }
    //  return mediumList;
    //}

    //private OntClass findMatchingConcept(OntModel m, SqlConnection con, string keyword)
    //  {
    //      OntClass k = null;
    //      OntClass pref = null;
    //      OntClass medium = m.getOntClass(media);
    //      string strippedName = null;
    //      string mediump = null;
    //      if (medium != null)
    //      {
    //          for (ExtendedIterator mediumInst = medium.listInstances(); mediumInst.hasNext(); )
    //          {

    //              Individual i = (Individual)mediumInst.next();
    //              mediump = i.getLabel("en");
    //              if (keyword.Contains("(" + mediump + ")"))
    //              {
    //                  strippedName = keyword.Replace(" (" + mediump + ")", "");
    //                  break;
    //              }
    //          }
    //      }

    //      for (ExtendedIterator allClassesItr = m.listClasses(); allClassesItr.hasNext(); )
    //      {

    //          k = (OntClass)allClassesItr.next();
    //          string classLabel = k.getLabel("en");

    #endregion

    #endregion
}

