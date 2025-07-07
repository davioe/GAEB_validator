# GAEB XML Validator (.NET)

This is a C# implementation of the [GAEB XML Validator](https://github.com/BaukoDoz/GAEB-XML-Validator), designed to validate GAEB XML files against their corresponding XSD schema definitions.

## About

This tool validates GAEB (Common Committee for Electronics in Construction) XML files against their official schema definitions. It's particularly useful for ensuring that your GAEB XML files conform to the required format and structure before processing them further.

## Features

- Validates GAEB XML files against official XSD schemas
- Automatically detects and uses the correct XSD schema based on XML namespace
- Handles recursive schema definitions and includes
- Generates detailed validation reports in JSON format
- Provides clear error messages with line references

## Requirements

- .NET 8.0 or higher
- GAEB XSD schema files (included in the `GAEB-XSD_schema_files` directory)

## Usage

- validate_gaeb.exe <path/to/XML-file>

## Output

The validator produces two types of output:
1. Console messages indicating whether the validation was successful
2. A `validation_results.json` file containing detailed results including:
   - Name of the checked file
   - Timestamp of validation
   - Validation status
   - Detailed error messages (if any)
   - List of used XSD files

## Credits

This is a C# port of the original [GAEB XML Validator](https://github.com/BaukoDoz/GAEB-XML-Validator) project. The implementation has been adapted to take advantage of .NET features while maintaining the same validation functionality.