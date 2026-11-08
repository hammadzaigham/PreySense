using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;

namespace PreySense.Core.Hardware;

public interface IPreySenseDriverExtractor
{
    string DriverFilePath { get; }
    bool EnsureDriverExtracted(out string? extractedPath, out string? errorMessage);
}

public class PreySenseDriverExtractor : IPreySenseDriverExtractor
{
    public const string DefaultResourceName = "PreySense.Core.Resources.embedded_x64.sys";
    public const string DefaultDriverFileName = "PreySenseCpuDriver.sys";

    private readonly string _targetDirectory;
    private readonly string _driverFilePath;
    private readonly Assembly _resourceAssembly;
    private readonly string _resourceName;

    public string DriverFilePath => _driverFilePath;

    public PreySenseDriverExtractor(
        string? targetDirectory = null,
        string? driverFileName = null,
        Assembly? resourceAssembly = null,
        string? resourceName = null)
    {
        _resourceAssembly = resourceAssembly ?? typeof(PreySenseDriverExtractor).Assembly;
        _resourceName = resourceName ?? DefaultResourceName;

        if (string.IsNullOrWhiteSpace(targetDirectory))
        {
            string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            if (string.IsNullOrWhiteSpace(programData))
                throw new InvalidOperationException("The system driver storage directory is unavailable.");
            programData = Path.Combine(programData, "PreySense");
            _targetDirectory = Path.Combine(programData, "Drivers");
        }
        else
        {
            _targetDirectory = targetDirectory;
        }

        string fileName = string.IsNullOrWhiteSpace(driverFileName) ? DefaultDriverFileName : driverFileName;
        _driverFilePath = Path.Combine(_targetDirectory, fileName);
    }

    public bool EnsureDriverExtracted(out string? extractedPath, out string? errorMessage)
    {
        extractedPath = null;
        errorMessage = null;

        try
        {
            using Stream? resourceStream = _resourceAssembly.GetManifestResourceStream(_resourceName);
            if (resourceStream == null)
            {
                errorMessage = $"Embedded driver resource '{_resourceName}' was not found in assembly '{_resourceAssembly.FullName}'.";
                return false;
            }

            using var ms = new MemoryStream();
            resourceStream.CopyTo(ms);
            byte[] embeddedBytes = ms.ToArray();

            if (embeddedBytes.Length == 0)
            {
                errorMessage = $"Embedded driver resource '{_resourceName}' is empty.";
                return false;
            }

            Directory.CreateDirectory(_targetDirectory);

            if (File.Exists(_driverFilePath))
            {
                try
                {
                    byte[] existingBytes = File.ReadAllBytes(_driverFilePath);
                    if (existingBytes.Length == embeddedBytes.Length)
                    {
                        byte[] existingHash = SHA256.HashData(existingBytes);
                        byte[] embeddedHash = SHA256.HashData(embeddedBytes);
                        if (existingHash.AsSpan().SequenceEqual(embeddedHash))
                        {
                            extractedPath = _driverFilePath;
                            return true;
                        }
                    }
                }
                catch (IOException)
                {
                    extractedPath = _driverFilePath;
                    return true;
                }
            }

            File.WriteAllBytes(_driverFilePath, embeddedBytes);
            extractedPath = _driverFilePath;
            return true;
        }
        catch (UnauthorizedAccessException ex)
        {
            errorMessage = $"Access denied extracting driver to '{_driverFilePath}'. Administrative privileges may be required: {ex.Message}";
            return false;
        }
        catch (Exception ex)
        {
            errorMessage = $"Failed to extract bundled driver to '{_driverFilePath}': {ex.Message}";
            return false;
        }
    }
}
