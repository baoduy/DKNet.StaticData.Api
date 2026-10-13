#!/usr/bin/env bash
set -euo pipefail
helm dependency build helm/dknet-staticdata
helm lint helm/dknet-staticdata
helm unittest helm/dknet-staticdata
helm template proof helm/dknet-staticdata >/dev/null
helm template proof helm/dknet-staticdata --set api.configMap.Database__Provider=SqlServer >/dev/null
helm template proof helm/dknet-staticdata --set api.configMap.BlobStorage__Provider=AzureStorage >/dev/null
helm template proof helm/dknet-staticdata --set api.configMap.BlobStorage__Provider=AwsS3 >/dev/null
helm template proof helm/dknet-staticdata --set api.httpRoute.enabled=true >/dev/null
