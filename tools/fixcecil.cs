using System; using System.Linq; using Mono.Cecil; using Mono.Cecil.Cil;
class F{ static void Main(string[] a){
  var m=ModuleDefinition.ReadModule(a[0],new ReaderParameters{ReadingMode=ReadingMode.Immediate});
  var rev1=m.ImportReference(typeof(Array).GetMethod("Reverse",new[]{typeof(Array)}));
  var rev3=m.ImportReference(typeof(Array).GetMethod("Reverse",new[]{typeof(Array),typeof(int),typeof(int)}));
  var split=m.ImportReference(typeof(string).GetMethod("Split",new[]{typeof(char[]),typeof(StringSplitOptions)}));
  // helper: static string[] __SplitCompat(string s, char c, StringSplitOptions o) { return s.Split(new char[]{c}, o); }
  var holder=new TypeDefinition("Mono.Cecil","__NetFxCompat",TypeAttributes.NotPublic|TypeAttributes.Abstract|TypeAttributes.Sealed,m.ImportReference(typeof(object)));
  m.Types.Add(holder);
  var h=new MethodDefinition("SplitCompat",MethodAttributes.Assembly|MethodAttributes.Static|MethodAttributes.HideBySig,m.ImportReference(typeof(string[])));
  h.Parameters.Add(new ParameterDefinition(m.ImportReference(typeof(string))));
  h.Parameters.Add(new ParameterDefinition(m.ImportReference(typeof(char))));
  h.Parameters.Add(new ParameterDefinition(m.ImportReference(typeof(StringSplitOptions))));
  var p=h.Body.GetILProcessor();
  p.Emit(OpCodes.Ldarg_0); p.Emit(OpCodes.Ldc_I4_1); p.Emit(OpCodes.Newarr,m.ImportReference(typeof(char)));
  p.Emit(OpCodes.Dup); p.Emit(OpCodes.Ldc_I4_0); p.Emit(OpCodes.Ldarg_1); p.Emit(OpCodes.Stelem_I2);
  p.Emit(OpCodes.Ldarg_2); p.Emit(OpCodes.Callvirt,split); p.Emit(OpCodes.Ret);
  holder.Methods.Add(h);
  int n=0;
  foreach(var t in m.GetTypes()) foreach(var md in t.Methods) if(md.HasBody && md!=h)
    foreach(var ins in md.Body.Instructions){
      var r=ins.Operand as MethodReference; if(r==null) continue;
      var dt=r.DeclaringType.FullName;
      if(dt=="System.Array" && r.Name=="Reverse" && r is GenericInstanceMethod){
        ins.Operand = r.Parameters.Count==1 ? rev1 : rev3; ins.OpCode=OpCodes.Call; n++;
        Console.WriteLine("  Reverse  in "+md.FullName);
      } else if(dt=="System.String" && r.Name=="Split" && r.Parameters.Count==2 && r.Parameters[0].ParameterType.FullName=="System.Char"){
        ins.Operand=h; ins.OpCode=OpCodes.Call; n++;
        Console.WriteLine("  Split    in "+md.FullName);
      }
    }
  m.Write(a[1]); Console.WriteLine("replaced "+n);
}}
