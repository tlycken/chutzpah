﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Chutzpah.Wrappers;

namespace Chutzpah
{
    public class HtmlTestFileCreator : IHtmlTestFileCreator
    {
        private readonly IFileSystemWrapper fileSystem;
        private readonly IFileProbe fileProbe;

        private readonly Regex ReferencePathRegex = new Regex(@"^\s*///[^\S\r\n]*<[^\S\r\n]*reference[^\S\r\n]+path[^\S\r\n]*=[^\S\r\n]*[""'](?<Path>[^""<>|]+)[""'][^\S\r\n]*/>",
                                                              RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public HtmlTestFileCreator()
            : this(new FileSystemWrapper(), new FileProbe())
        {
        }

        public HtmlTestFileCreator(IFileSystemWrapper fileSystem, IFileProbe fileProbe)
        {
            this.fileSystem = fileSystem;
            this.fileProbe = fileProbe;
        }

        public string CreateTestFile(string jsFile)
        {
            if (string.IsNullOrWhiteSpace(jsFile))
                throw new ArgumentNullException("jsFile");
            if (!Path.GetExtension(jsFile).Equals(".js", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Expecting a .js file");

            string jsFilePath = fileProbe.FindPath(jsFile);
            if (jsFilePath == null)
                throw new FileNotFoundException("Unable to find file: " + jsFile);

            var tempFolder = fileSystem.GetTemporayFolder();
            var testHtmlFilePath = Path.Combine(tempFolder, "test.html");
            var qunitFilePath = Path.Combine(tempFolder, "qunit.js");
            var qunitCssFilePath = Path.Combine(tempFolder, "qunit.css");
            var uniqueJsFileName = MakeUnique(Path.GetFileName(jsFile));
            var testFilePath = Path.Combine(tempFolder, uniqueJsFileName);

            var testHtmlTemplate = EmbeddedManifestResourceReader.GetEmbeddedResoureText<TestRunner>("Chutzpah.TestFiles.testTemplate.html");
            var quintText = EmbeddedManifestResourceReader.GetEmbeddedResoureText<TestRunner>("Chutzpah.TestFiles.qunit.js");
            var quintCssText = EmbeddedManifestResourceReader.GetEmbeddedResoureText<TestRunner>("Chutzpah.TestFiles.qunit.css");
            var testFileText = fileSystem.GetText(jsFilePath);

            IEnumerable<string> referencedFilePaths = GetAndCopyReferencedFiles(testFileText, jsFilePath, tempFolder);

            string testHtmlText = FillTestHtmlTemplate(testHtmlTemplate, uniqueJsFileName, referencedFilePaths);

            fileSystem.Save(testFilePath, testFileText);
            fileSystem.Save(testHtmlFilePath, testHtmlText);
            fileSystem.Save(qunitFilePath, quintText);
            fileSystem.Save(qunitCssFilePath, quintCssText);

            return testHtmlFilePath;
        }

        private IEnumerable<string> GetAndCopyReferencedFiles(string testFileText, string testFilePath, string tempFolder)
        {
            var paths = new List<string>();
            foreach (Match match in ReferencePathRegex.Matches(testFileText))
            {
                if (match.Success)
                {
                    string referencePath = match.Groups["Path"].Value;
                    Uri referenceUri = new Uri(referencePath,UriKind.RelativeOrAbsolute);
                    if (!referenceUri.IsAbsoluteUri || referenceUri.IsFile)
                    {
                        string relativeReferencePath = Path.Combine(Path.GetDirectoryName(testFilePath), referencePath);
                        var absolutePath = fileProbe.FindPath(relativeReferencePath);
                        if (absolutePath != null)
                        {
                            var uniqueFileName = MakeUnique(Path.GetFileName(referencePath));
                            fileSystem.CopyFile(absolutePath, Path.Combine(tempFolder, uniqueFileName));
                            paths.Add(uniqueFileName);
                        }
                    }
                    else if (referenceUri.IsAbsoluteUri)
                    {
                        paths.Add(referencePath);
                    }
                }
            }
            return paths;
        }

        private static string FillTestHtmlTemplate(string testHtmlTemplate, string testFilePath, IEnumerable<string> referencePaths)
        {
            var referenceReplacement = new StringBuilder();
            foreach (string referencePath in referencePaths)
            {
                referenceReplacement.AppendLine(GetScriptStatement(referencePath));
            }

            testHtmlTemplate = testHtmlTemplate.Replace("@@TestFiles@@", GetScriptStatement(testFilePath));
            testHtmlTemplate = testHtmlTemplate.Replace("@@ReferencedFiles@@", referenceReplacement.ToString());


            return testHtmlTemplate;
        }

        public static string GetScriptStatement(string path)
        {
            const string format = @"<script type=""text/javascript"" src=""{0}""></script>";
            return string.Format(format, path);
        }

        private string MakeUnique(string fileName)
        {
            var randomFileName = fileSystem.GetRandomFileName();
            return string.Format("{0}_{1}", randomFileName, fileName);
        }
    }
}