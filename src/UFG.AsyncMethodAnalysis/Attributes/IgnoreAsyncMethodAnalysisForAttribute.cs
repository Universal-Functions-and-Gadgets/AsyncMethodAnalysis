namespace UFG.AsyncMethodAnalysis.Attributes;

[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public class IgnoreAsyncMethodAnalysisForAttribute : Attribute
{
   public string FullTypeName { get; }

   public IgnoreAsyncMethodAnalysisForAttribute(string fullTypeName)
   {
      FullTypeName = fullTypeName;
   }
}
