# Build
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore ./Jellywatch.Api.csproj -v minimal && \
    dotnet publish ./Jellywatch.Api.csproj -c Release -o /out --no-restore -v minimal

# Runtime
FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
COPY --from=build /out .

RUN mkdir -p /app/data /app/data/images

VOLUME ["/app/data"]

ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

EXPOSE 8080

ENTRYPOINT ["dotnet", "Jellywatch.Api.dll"]
