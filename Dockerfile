# ===== Build stage =====
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY src/PetHospital.Web/PetHospital.Web.csproj src/PetHospital.Web/
RUN dotnet restore src/PetHospital.Web/PetHospital.Web.csproj
COPY src/ src/
# Không kèm file .pdb (thông tin debug) trong bản phát hành
RUN dotnet publish src/PetHospital.Web/PetHospital.Web.csproj -c Release -o /app/publish /p:UseAppHost=false /p:DebugType=None /p:DebugSymbols=false

# ===== Runtime stage =====
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080 \
    TZ=Asia/Ho_Chi_Minh
EXPOSE 8080
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "PetHospital.Web.dll"]
