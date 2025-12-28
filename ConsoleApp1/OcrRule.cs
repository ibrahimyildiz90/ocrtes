using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ConsoleApp1
{
    public enum ExtractionType { TableColumnList, TableColumnSum, SingleValue }
    public class OcrRule
    {
        public string FieldName { get; set; }        // Örn: "Kısmi Tevkifat İşlem Türü"
        public string StartAnchor { get; set; }      // Örn: "KISMİ TEVKİFAT UYGULANAN İŞLEMLER"
        public string ColumnHeader { get; set; }     // Örn: "İşlem Türü"
        public string RightLimitHeader { get; set; } // Örn: "Matrah" (Genişlik sınırı için)
                                                     // 
        // RightLimitHeader ile belirtilen kolon başlığının başlangıç noktası X olsun.
        // X-RightLimitHeaderXOffset kadar x offset belirliyoruz.
        // Bununla aslında sağdaki kolon başığının ne kadar soluna kadar okumalıyız. Onun kararını veriyoruz
        public int? RightLimitHeaderXOffset { get; set; } 
        public string EndAnchor { get; set; }        // Örn: "Matrah Toplamı"

        public int XOffset { get; set; }             // Başlıktan ne kadar sağa/sola kayacak       
        public int? ManualWidth { get; set; }        // Opsiyonel: El ile genişlik vermek istenirse
        public ExtractionType Type { get; set; }
    }
}
