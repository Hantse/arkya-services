# syntax=docker/dockerfile:1
FROM nginxinc/nginx-unprivileged:1.29.2-alpine@sha256:031d8c8ab79d2d171a2a383aa50a06f527791c05ba1aeae9ca05d7b9779caf8e AS final
ENV NGINX_ENTRYPOINT_LOCAL_RESOLVERS=1 \
    ARKYA_UNITY_UPSTREAM=arkya-web-client.arkya-realm-beta-web.svc.cluster.local:8080 \
    NGINX_ENVSUBST_FILTER=^(NGINX_LOCAL_RESOLVERS|ARKYA_UNITY_UPSTREAM)$ \
    NGINX_ENVSUBST_OUTPUT_DIR=/tmp
COPY nginx.conf /etc/nginx/templates/nginx.conf.template
COPY dist/ /usr/share/nginx/html/
USER 101:101
EXPOSE 8080
CMD ["nginx", "-c", "/tmp/nginx.conf", "-g", "daemon off;"]
