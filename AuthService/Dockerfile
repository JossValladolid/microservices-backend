FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY AuthService.csproj ./
RUN dotnet restore

COPY . .
RUN dotnet publish -c Release -o /app/publish --no-restore


FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "AuthService.dll"]


FROM mcr.microsoft.com/dotnet/sdk:10.0 AS dev
WORKDIR /src

COPY AuthService.csproj ./
RUN dotnet restore

COPY . .

ENV ASPNETCORE_URLS=http://+:8080

ENV DOTNET_USE_POLLING_FILE_WATCHER=1
EXPOSE 8080

ENTRYPOINT ["dotnet", "watch", "run", "--no-launch-profile", "--urls", "http://+:8080"]