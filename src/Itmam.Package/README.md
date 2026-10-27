# Itmam.Package

Windows Application Packaging Project (`.wapproj`) that produces the MSIX for the Microsoft Store.

The project file is added in **Phase 7**. Building it requires Visual Studio with the MSIX packaging tooling
(workload *Windows application development* / component *MSIX Packaging Tools*), because `dotnet build`
cannot build `.wapproj` projects. CI builds `Itmam.CI.slnf`, which excludes this project.
