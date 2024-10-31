FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 5115

ENV ASPNETCORE_URLS=http://+:5115

USER app
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG configuration=Release
WORKDIR /src
COPY ["UserDatabaseInterface.csproj", "./"]
RUN dotnet restore "UserDatabaseInterface.csproj"
COPY . .
WORKDIR "/src/."
RUN dotnet build "UserDatabaseInterface.csproj" -c $configuration -o /app/build

FROM build AS publish
ARG configuration=Release
RUN dotnet publish "UserDatabaseInterface.csproj" -c $configuration -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "UserDatabaseInterface.dll"]
