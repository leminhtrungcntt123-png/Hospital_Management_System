FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props Directory.Packages.props global.json ./
COPY src/HIS.Domain/HIS.Domain.csproj src/HIS.Domain/
COPY src/HIS.DAL/HIS.DAL.csproj src/HIS.DAL/
COPY src/HIS.BLL/HIS.BLL.csproj src/HIS.BLL/
COPY src/HIS.Web/HIS.Web.csproj src/HIS.Web/

RUN dotnet restore src/HIS.Web/HIS.Web.csproj

COPY src/ src/
RUN dotnet publish src/HIS.Web/HIS.Web.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080

COPY --from=build /app/publish .

ENTRYPOINT ["sh", "-c", "ASPNETCORE_URLS=http://+:${PORT:-8080} exec dotnet HIS.Web.dll"]

