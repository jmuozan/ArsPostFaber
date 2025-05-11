using System;
using System.IO;
using System.Reflection;
using System.Text;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace LLM.OllamaComps
{
    public static class PdfContextManager
    {
        // Base folder for PDF context, relative to application base directory
        private static readonly string _contextFolderPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "context_for_llm");

        public static string LoadPdfContext(string[] specificFiles = null)
        {
            StringBuilder contextBuilder = new StringBuilder();

            if (!Directory.Exists(_contextFolderPath))
            {
                Directory.CreateDirectory(_contextFolderPath);
                return "No context files found. Created context_for_llm folder.";
            }

            string[] pdfFiles = specificFiles ??
                Directory.GetFiles(_contextFolderPath, "*.pdf", SearchOption.TopDirectoryOnly);

            foreach (string pdfPath in pdfFiles)
            {
                try
                {
                    using (PdfDocument document = PdfDocument.Open(pdfPath))
                    {
                        contextBuilder.AppendLine($"=== CONTEXT FROM: {Path.GetFileName(pdfPath)} ===");

                        for (int i = 1; i <= document.NumberOfPages; i++)
                        {
                            var page = document.GetPage(i);
                            string text = ContentOrderTextExtractor.GetText(page);
                            contextBuilder.AppendLine(text);
                        }

                        contextBuilder.AppendLine("=== END CONTEXT ===\n");
                    }
                }
                catch (Exception ex)
                {
                    contextBuilder.AppendLine($"Error loading PDF {Path.GetFileName(pdfPath)}: {ex.Message}");
                }
            }

            return contextBuilder.ToString();
        }
    }
}