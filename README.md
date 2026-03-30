**VSFormsManager**

VSFormsManager is a small Windows Forms utility that uses AI to convert from one Form format 
to another and allow copying forms from project to project. 


Wish List
Batch copy — select multiple forms from the tree and copy them all to a target project folder at once, with a single dependency review pass
Dependency resolution — if the target is a known .csproj, you could scan its source tree to auto-detect which of the flagged namespaces actually exist there
Conversion history — log what was converted to what, so you can re-run or audit later
VS Extension — the parsing and conversion logic is cleanly separated from the UI, so wrapping it in a VSIX extension and calling it from right-click in Solution Explorer would be straightforward
