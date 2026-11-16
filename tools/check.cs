using System; using System.IO; using System.Linq; using System.Collections.Generic; using Mono.Cecil; using Mono.Cecil.Cil;
class R : IAssemblyResolver {
  string dir; Dictionary<string,AssemblyDefinition> c=new Dictionary<string,AssemblyDefinition>();
  public R(string d){dir=d;}
  public AssemblyDefinition Resolve(AssemblyNameReference n){return Resolve(n,new ReaderParameters{AssemblyResolver=this});}
  public AssemblyDefinition Resolve(AssemblyNameReference n, ReaderParameters p){
    if(c.ContainsKey(n.Name)) return c[n.Name];
    var f=Path.Combine(dir,n.Name+".dll"); if(!File.Exists(f)) f=Path.Combine(dir,"Facades",n.Name+".dll");
    if(!File.Exists(f)) return c[n.Name]=null;
    return c[n.Name]=AssemblyDefinition.ReadAssembly(f,new ReaderParameters{AssemblyResolver=this});
  }
  public void Dispose(){}
}
class T{ static int Main(string[] a){
  var m=ModuleDefinition.ReadModule(a[0],new ReaderParameters{AssemblyResolver=new R(a[1])});
  int bad=0; var seen=new HashSet<string>();
  foreach(var mr in m.GetMemberReferences()){
    var scope=mr.DeclaringType.Scope.Name;
    if(!(scope=="mscorlib"||scope.StartsWith("System")||scope=="netstandard")) continue;
    object r=null; try{ r = mr is MethodReference ? (object)((MethodReference)mr).Resolve() : (object)((FieldReference)mr).Resolve(); }catch{}
    if(r==null && seen.Add(mr.FullName)){ bad++; Console.WriteLine("MISSING "+mr.FullName); }
  }
  foreach(var tr in m.GetTypeReferences()){
    var s=tr.Scope.Name; if(!(s=="mscorlib"||s.StartsWith("System"))) continue;
    TypeDefinition td=null; try{td=tr.Resolve();}catch{}
    if(td==null){bad++;Console.WriteLine("MISSING TYPE "+tr.FullName);}
  }
  Console.WriteLine(a[0]+": missing="+bad); return bad;
}}
