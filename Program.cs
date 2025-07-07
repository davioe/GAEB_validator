// MIT License
// Copyright (c) 2025 davioe
// This is a C# implementation of the GAEB XML Validator (https://github.com/BaukoDoz/GAEB-XML-Validator)

namespace GAEB_validator
{
    using Newtonsoft.Json;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text.RegularExpressions;
    using System.Xml;
    using System.Xml.Linq;
    using System.Xml.Schema;

    namespace GAEBValidator
    {
        class Program
        {
            static List<string> matchedXsdFiles = new List<string>();
            static List<object> validationErrors = new List<object>();
            private static string currentXmlFile;

            static void Main(string[] args)
            {
                if (args.Length != 1)
                {
                    Console.WriteLine("Verwendung: validate_gaeb.exe <Pfad/zur/XML-Datei>");
                    return;
                }

                currentXmlFile = Path.GetFullPath(args[0]);
                string schemaDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "GAEB-XSD_schema_files");

                try
                {
                    ValidateXml(currentXmlFile, schemaDir);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Fehler: " + ex.Message);
                }
            }

            /// <summary>
            /// Retrieves all XSD files from the specified directory.
            /// </summary>
            /// <param name="schemaDirectory">The path to the directory containing the XSD files. The directory must exist and be accessible.</param>
            /// <returns>An array of file paths representing the XSD files in the specified directory.  If no XSD files are
            /// found, the array will be empty.</returns>
            /// <exception cref="DirectoryNotFoundException">Thrown if the specified <paramref name="schemaDirectory"/> does not exist.</exception>
            /// <exception cref="UnauthorizedAccessException">Thrown if the application does not have permission to access the specified <paramref
            /// name="schemaDirectory"/>.</exception>
            private static string[] GetXsdFilesWithAssert(string schemaDirectory)
            {
                // Check if directory exists
                if (!Directory.Exists(schemaDirectory))
                {
                    throw new DirectoryNotFoundException($"Schema-Verzeichnis nicht gefunden: {schemaDirectory}");
                }

                try
                {
                    return Directory.GetFiles(schemaDirectory, "*.xsd");
                }
                catch (UnauthorizedAccessException ex)
                {
                    throw new UnauthorizedAccessException($"Keine Berechtigung für Zugriff auf: {schemaDirectory}", ex);
                }
            }

            /// <summary>
            /// Validates an XML file against its corresponding XML Schema Definition (XSD) files.
            /// </summary>
            /// <remarks>This method performs the following steps: <list type="number"> <item>Checks
            /// the existence and syntax of the XML file.</item> <item>Identifies the appropriate XSD file based on the
            /// XML file's target namespace.</item> <item>Recursively collects all linked XSD files required for
            /// validation.</item> <item>Compiles the XSD schema set to ensure consistency.</item> <item>Validates the
            /// XML file against the compiled schema set.</item> </list> Validation errors are reported via a callback
            /// mechanism. Ensure that the XML file and XSD files are properly configured for validation.</remarks>
            /// <param name="xmlPath">The path to the XML file to be validated. The file must exist and be well-formed.</param>
            /// <param name="schemaDirectory">The directory containing the XSD files used for validation. The directory must include the XSD file
            /// matching the target namespace of the XML file.</param>
            /// <exception cref="FileNotFoundException">Thrown if the XML file specified by <paramref name="xmlPath"/> does not exist, or if no matching XSD
            /// file is found in <paramref name="schemaDirectory"/>.</exception>
            /// <exception cref="Exception">Thrown if the XML file does not specify a namespace, or if an error occurs during validation.</exception>
            static void ValidateXml(string xmlPath, string schemaDirectory)
            {
                // 1) File checks as usual
                if (!File.Exists(xmlPath))
                    throw new FileNotFoundException($"XML-Datei nicht gefunden: {xmlPath}");

                XDocument doc;
                try
                {
                    doc = XDocument.Load(xmlPath);
                }
                catch (XmlException ex)
                {
                    throw new Exception($"XML-Syntaxfehler: {ex.Message}");
                }

                XNamespace ns = doc.Root?.Name.Namespace ?? throw new Exception("Kein Namespace gefunden.");

                // 2) Find XSD files
                var xsdFiles = GetXsdFilesWithAssert(schemaDirectory)
                    .Select(f =>
                    {
                        string content = File.ReadAllText(f);

                        var match = Regex.Match(content, "targetNamespace=\"(.*?)\"");
                        return new { Path = f, TargetNamespace = match.Success ? match.Groups[1].Value : null };
                    })
                    .Where(x => x.TargetNamespace != null)
                    .ToList();

                var matched = xsdFiles.FirstOrDefault(x => x.TargetNamespace == ns.NamespaceName)
                              ?? throw new FileNotFoundException($"Keine passende XSD mit Namespace {ns} gefunden.");

                // 3) Recursively collect linked XSDs
                FindMatchingXsds(matched.Path, schemaDirectory, new HashSet<string>());

                // 4) Create SchemaSet and set resolver
                var schemaSet = new XmlSchemaSet
                {
                    XmlResolver = new XmlUrlResolver()  // important for relative paths in redefine/include
                };

                // 5) Add all found XSDs with BaseUri
                foreach (var xsd in matchedXsdFiles.Distinct())
                {
                    using var stream = File.OpenRead(xsd);
                    var readerSettings = new XmlReaderSettings
                    {
                        DtdProcessing = DtdProcessing.Ignore
                    };
                    string baseUri = new Uri(Path.GetFullPath(xsd)).AbsoluteUri;
                    using var reader = XmlReader.Create(stream, readerSettings, baseUri);
                    schemaSet.Add(null, reader);
                }

                // 6) Compile schema (finds inconsistencies early)
                schemaSet.Compile();

                // 8) Perform validation
                var valSettings = new XmlReaderSettings
                {
                    ValidationType = ValidationType.Schema,
                    Schemas = schemaSet,
                    XmlResolver = new XmlUrlResolver()
                };
                valSettings.ValidationEventHandler += ValidationCallback;

                // 8) Perform validation
                using (var reader = XmlReader.Create(xmlPath, valSettings))
                {
                    while (reader.Read()) { /* Callback side-effects */ }
                }

                // 9) Save results
                SaveResults(xmlPath);
            }


            /// <summary>
            /// Recursively finds and collects XSD files that are linked through <c>xs:redefine</c> elements.
            /// </summary>
            /// <remarks>This method reads the content of the specified XSD file and searches for
            /// <c>xs:redefine</c> elements that reference other schema files. For each referenced schema file, the
            /// method recursively processes it if it exists in the specified schema directory. The <paramref
            /// name="visited"/> parameter ensures that each file is processed only once.</remarks>
            /// <param name="xsdPath">The path to the initial XSD file to process.</param>
            /// <param name="schemaDir">The directory containing the schema files referenced by <c>xs:redefine</c> elements.</param>
            /// <param name="visited">A set of already visited XSD file paths to prevent redundant processing and infinite recursion.</param>
            static void FindMatchingXsds(string xsdPath, string schemaDir, HashSet<string> visited)
            {
                if (visited.Contains(xsdPath)) return;
                visited.Add(xsdPath);
                matchedXsdFiles.Add(xsdPath);

                string content = File.ReadAllText(xsdPath);
                var matches = Regex.Matches(content, "<xs:redefine\\s+schemaLocation=\"(.*?)\"");

                foreach (Match match in matches)
                {
                    string relative = match.Groups[1].Value;
                    string linkedPath = Path.Combine(schemaDir, relative);
                    if (File.Exists(linkedPath))
                    {
                        FindMatchingXsds(linkedPath, schemaDir, visited);
                    }
                }
            }

            /// <summary>
            /// Handles XML validation events and processes validation errors.
            /// </summary>
            /// <remarks>This callback processes validation errors encountered during XML schema
            /// validation. It extracts relevant information, such as the error message, line number, and source URI,
            /// and categorizes the error with a hint based on its type. The processed error details are added to a
            /// collection for further handling.</remarks>
            /// <param name="sender">The source of the validation event, typically the XML reader or schema validator.</param>
            /// <param name="e">The <see cref="ValidationEventArgs"/> containing details about the validation error.</param>
            static void ValidationCallback(object sender, ValidationEventArgs e)
            {
                var ex = e.Exception;
                string? line = null;

                if (ex.LineNumber > 0)
                {
                    var lines = File.ReadAllLines(currentXmlFile);
                    if (ex.LineNumber - 1 < lines.Length)
                        line = lines[ex.LineNumber - 1].Trim();
                }

                string hint;
                string msg = e.Message.ToLowerInvariant();
                if (msg.Contains("unexpected child"))
                {
                    hint = "Ein unerwartetes Element wurde gefunden.";
                }
                else if (msg.Contains("value") && msg.Contains("must be one of"))
                {
                    hint = "Ein ungültiger Wert wurde gefunden.";
                }
                else
                {
                    hint = "Allgemeiner Validierungsfehler.";
                }

                validationErrors.Add(new
                {
                    path = ex.SourceUri,
                    message = $"{e.Message} Hinweis: {hint}",
                    full_line = line
                });
            }

            /// <summary>
            /// Saves the validation results of an XML file to a JSON file and provides a summary of the validation
            /// outcome.
            /// </summary>
            /// <remarks>The method generates a JSON file named <c>validation_results.json</c>
            /// containing details about the validation process, including the file name, timestamp, validation status,
            /// errors (if any), and the XSD files used for validation. If the XML file is valid, a success message is
            /// displayed in the console. Otherwise, the number of validation errors is displayed, and the user is
            /// directed to the JSON file for detailed error information.</remarks>
            /// <param name="xmlPath">The file path of the XML document to validate. Must be a valid path to an existing XML file.</param>
            static void SaveResults(string xmlPath)
            {
                var result = new
                {
                    checked_file = Path.GetFileName(xmlPath),
                    timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    is_valid = validationErrors.Count == 0,
                    errors = validationErrors,
                    used_xsd_files = matchedXsdFiles
                };

                File.WriteAllText("validation_results.json", JsonConvert.SerializeObject(result, Newtonsoft.Json.Formatting.Indented));

                if (validationErrors.Count == 0)
                {
                    Console.WriteLine("Die XML-Datei ist gültig.");
                }
                else
                {
                    Console.WriteLine($"Es wurden {validationErrors.Count} Fehler gefunden. Siehe 'validation_results.json'.");
                }
            }
        }
    }

}
