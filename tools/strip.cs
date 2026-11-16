using Mono.Cecil;
class S{ static void Main(string[] a){
  var m=ModuleDefinition.ReadModule(a[0]);
  var n=m.Assembly.Name; n.PublicKey=new byte[0]; n.HasPublicKey=false;
  m.Attributes &= ~ModuleAttributes.StrongNameSigned;
  m.Write(a[1]); System.Console.WriteLine("stripped: "+m.Assembly.FullName);
}}
