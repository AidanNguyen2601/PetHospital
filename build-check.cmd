@echo off
rem Kiem tra bien dich ma nguon PetHospital (ghi ket qua vao build.log)
cd /d "%~dp0"
echo [%date% %time%] dotnet build > build.log
where dotnet >> build.log 2>&1
dotnet build src\PetHospital.Web\PetHospital.Web.csproj -c Release -nologo -clp:NoSummary >> build.log 2>&1
echo EXITCODE=%errorlevel% >> build.log
exit
