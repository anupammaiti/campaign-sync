﻿using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using Cwm.AdobeCampaign.Templates.Exceptions;
using Cwm.AdobeCampaign.Templates.Model;

namespace Cwm.AdobeCampaign.Templates.Services.Metadata
{
    /// <summary>
    /// Provides functions for processing XML files.
    /// </summary>
    public class XmlMetadataProcessor : IXmlMetadataExtractor, IMetadataInserter
    {
        #region Fields

        private static readonly Regex MetadataCommentRegex = new Regex("^!(?<value>.*)!$", RegexOptions.Singleline);

        private static readonly string MetadataFormat = string.Format("!{0}{{0}}{0}!", Environment.NewLine);

        private readonly IMetadataParser _metadataParser;

        private readonly IMetadataFormatter _metadataFormatter;

        #endregion

        #region Constructor

        /// <summary>
        /// Initializes a new instance of <see cref="XmlMetadataProcessor"/>
        /// </summary>
        /// <param name="metadataParser">Metadata parser</param>
        /// <param name="metadataFormatter">Metadata formatter</param>
        public XmlMetadataProcessor(IMetadataParser metadataParser, IMetadataFormatter metadataFormatter)
        {
            _metadataParser = metadataParser;
            _metadataFormatter = metadataFormatter;
        }

        #endregion

        #region Methods

        /// <summary>
        /// Extract the code and metadata from raw XML file content.
        /// </summary>
        /// <param name="input">Raw XML file content</param>
        /// <returns>Class containing code content and metadata.</returns>
        /// <exception cref="MetadataException" />
        public Template ExtractMetadata(string input)
        {
            if (input == null)
            {
                throw new ArgumentNullException("input");
            }

            // Parse xml.
            var xmlDocument = new XmlDocument();
            try
            {
                xmlDocument.LoadXml(input);
            }
            catch (XmlException ex)
            {
                throw new MetadataException(ex);
            }

            // Read metadata from correctly formatted comment.
            var metadata = new TemplateMetadata();
            var commentNodes = xmlDocument.DocumentElement.SelectNodes("//comment()");
            if (commentNodes != null)
            {
                var found = 0;
                foreach (var commentNode in commentNodes.Cast<XmlComment>().Reverse())
                {
                    var match = MetadataCommentRegex.Match(commentNode.InnerText);
                    if (!match.Success)
                    {
                        continue;
                    }

                    found++;
                    if (found > 1)
                    {
                        continue;
                    }

                    commentNode.ParentNode.RemoveChild(commentNode);
                    metadata = _metadataParser.Parse(match.Groups["value"].Value);
                }

                if (found > 1)
                {
                    throw new MultipleMetadataException(found);
                }
            }

            var output = GenerateDocumentXml(xmlDocument);
            return new Template
            {
                Code = output,
                Metadata = metadata,
            };
        }

        /// <summary>
        /// Converts metadata and code into raw XML file content which can be written to disk.
        /// </summary>
        /// <param name="input">Metadata and code</param>
        /// <returns>Raw XML file content</returns>
        public string InsertMetadata(Template input)
        {
            if (input == null)
            {
                throw new ArgumentNullException("input");
            }

            // Parse xml.
            var xmlDocument = new XmlDocument();
            xmlDocument.LoadXml(input.Code);

            // Add comment to start of document.
            var metadataString = _metadataFormatter.Format(input.Metadata);
            var metadataCommentNode = xmlDocument.CreateComment(string.Format(MetadataFormat, metadataString));
            xmlDocument.PrependChild(metadataCommentNode);

            return GenerateDocumentXml(xmlDocument);
        }

        #endregion

        #region Helpers

        private static string GenerateDocumentXml(XmlDocument xmlDocument)
        {
            // Format nicely: indent, and omit xml declaration if it wasn't present in the original.
            var settings = new XmlWriterSettings
            {
                Indent = true,
                OmitXmlDeclaration = xmlDocument.FirstChild.NodeType != XmlNodeType.XmlDeclaration,
            };

            // Generate document as string.
            // All this complexity is to get it correctly UTF-8 encoded, as otherwise it ends up UTF-8 encoded but
            // declared as UTF-16. :-(
            using (var outputStream = new MemoryStream())
            using (var outputWriter = new StreamWriter(outputStream, Encoding.UTF8))
            using (var xmlWriter = XmlWriter.Create(outputWriter, settings))
            {
                xmlDocument.Save(xmlWriter);

                var outputReader = new StreamReader(outputStream);
                outputStream.Position = 0;
                var output = outputReader.ReadToEnd();
                return output;
            }
        }

        #endregion
    }
}
